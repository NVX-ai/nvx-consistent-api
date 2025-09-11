using EventStore.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Framework.Projections;
using Nvx.ConsistentAPI.Framework.StaticEndpoints;
using Nvx.ConsistentAPI.InternalTooling;
using HashCode = System.HashCode;

namespace Nvx.ConsistentAPI;

public interface Endpoint
{
  /// <summary>
  ///   Authentication options:
  ///   - Everyone: Everyone is allowed, for tenant bound commands, it overriden by `EveryoneAuthenticated`.
  ///   - EveryoneAuthenticated: Every authenticated user is allowed, still applies tenancy constraints.
  ///   - PermissionsRequireAll: Only users with all permissions referenced in the constructor are allowed.
  ///   - PermissionsRequireOne: Users with at least one of the permissions referenced in the constructor are allowed.
  /// </summary>
  AuthOptions Auth { get; }
}

public interface EventModelingCommandArtifact : Endpoint
{
  /// <summary>
  ///   Called by the framework to wire up the command.
  /// </summary>
  /// <param name="app">The web app that will expose the API.</param>
  /// <param name="fetcher">Entity fetcher.</param>
  /// <param name="emitter">Event emitter.</param>
  /// <param name="settings">Framework settings.</param>
  /// <param name="logger">Logger instance.</param>
  void ApplyTo(WebApplication app, Fetcher fetcher, Emitter emitter, GeneratorSettings settings, ILogger logger);
}

public interface EventModelingReadModelArtifact : Endpoint
{
  Type ShapeType { get; }
  Task<SingleReadModelInsights> Insights(ulong lastEventPosition, EventStoreClient eventStoreClien);

  Task ApplyTo(
    WebApplication app,
    EventStoreClient esClient,
    Fetcher fetcher,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    Emitter emitter,
    GeneratorSettings settings,
    ILogger logger);

  bool IsUpToDate(Position? position = null);
}

public interface IdempotentReadModel
{
  Task TryProcess(
    FoundEntity foundEntity,
    DatabaseHandlerFactory dbFactory,
    StrongId entityId,
    string? checkpoint,
    ILogger logger,
    CancellationToken cancellationToken);

  bool CanProject(EventModelEvent e);
  bool CanProject(string streamName);

  string TableName { get; }
}

public interface EventModelingProjectionArtifact
{
  /// <summary>
  ///   The projection name, used to define the subscription and to generate the idempotency keys.
  ///   <remarks>MUST BE UNIQUE PER PROJECTION AND SHOULD NEVER CHANGE</remarks>
  /// </summary>
  string Name { get; }

  string SourcePrefix { get; }
  bool CanProject(ResolvedEvent evt);

  Task HandleEvent(
    ResolvedEvent evt,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    Fetcher fetcher,
    EventStoreClient client);
}

public static class PermissionsEndpoint
{
  public static void ApplyTo(this string[] permissions, WebApplication app, GeneratorSettings settings)
  {
    if (!settings.EnabledFeatures.HasEndpoints())
    {
      return;
    }

    app
      .MapGet("/permissions", () => permissions)
      .WithTags(OperationTags.Authorization)
      .WithOpenApi(o =>
      {
        o.Description = "Returns a list of all the permissions used in the application.";
        return o;
      });
  }
}

public class EventModel
{
  private readonly RecurringTaskRunner runner = new();

  private TodoProcessor? processor;
  public EventModelingCommandArtifact[] Commands { private get; init; } = [];
  public EventModelingReadModelArtifact[] ReadModels { private get; init; } = [];
  public EventModelingProjectionArtifact[] Projections { private get; init; } = [];
  public TodoTaskDefinition[] Tasks { private get; init; } = [];
  public EntityDefinition[] Entities { private get; init; } = [];
  public RecurringTaskDefinition[] RecurringTasks { private get; init; } = [];
  public Ingestor[] Ingestors { private get; init; } = [];
  public StaticEndpointArtifact[] StaticEndpoints { private get; init; } = [];
  public InterestTrigger[] InterestTriggers { private get; init; } = [];
  public string? ApiName { get; init; }
  public string? ApiVersion { get; init; }
  internal string[] Prefixes => Entities.Select(e => e.StreamPrefix).Distinct().ToArray();

  private string[] Permissions =>
    Commands
      .SelectMany(c => PermissionsFor(c.Auth))
      .Concat(ReadModels.SelectMany(rm => PermissionsFor(rm.Auth)))
      .Distinct()
      .ToArray();

  private static string[] PermissionsFor(AuthOptions du) =>
    du.Match(_ => [], _ => [], arr => arr.Permissions, orr => orr.Permissions);

  public EventModel Merge(EventModel other) =>
    new()
    {
      Commands = Commands.Concat(other.Commands).ToArray(),
      ReadModels = ReadModels.Concat(other.ReadModels).ToArray(),
      Projections = Projections.Concat(other.Projections).ToArray(),
      Tasks = Tasks.Concat(other.Tasks).ToArray(),
      Entities = Entities.Concat(other.Entities).ToArray(),
      RecurringTasks = RecurringTasks.Concat(other.RecurringTasks).ToArray(),
      Ingestors = Ingestors.Concat(other.Ingestors).ToArray(),
      ApiName = other.ApiName ?? ApiName,
      ApiVersion = other.ApiVersion ?? ApiVersion,
      StaticEndpoints = StaticEndpoints.Concat(other.StaticEndpoints).ToArray(),
      InterestTriggers = InterestTriggers.Concat(other.InterestTriggers).ToArray()
    };

  public async Task<Fetcher> ApplyTo(WebApplication app, GeneratorSettings settings, ILogger logger)
  {
    var esClient = new EventStoreClient(EventStoreClientSettings.Create(settings.EventStoreConnectionString));
    var emitter = new Emitter(esClient, logger);
    var parser = Parser();
    var interestFetcher = new InterestFetcher(esClient, parser);

    var fetcher = new Fetcher(Entities.Select(e => e.GetFetcher(esClient, parser, interestFetcher)));

    await FileDefinitions.InitializeEndpoint(app, emitter, fetcher, settings);
    UserSecurityDefinitions.InitializeEndpoints(app, emitter, fetcher, settings);
    Permissions.ApplyTo(app, settings);

    foreach (var command in Commands)
    {
      command.ApplyTo(app, fetcher, emitter, settings, logger);
    }

    var projectionDaemon = new ProjectionDaemon(
      Projections
        .Concat(Tasks.Select(t => t.Projection))
        .Concat(RecurringTasks.Select(rt => rt.ToTodoTaskDefinition().Projection))
        .ToArray(),
      fetcher,
      emitter,
      esClient,
      parser,
      app,
      settings,
      logger);
    await projectionDaemon.Initialize();

    foreach (var readModel in ReadModels)
    {
      await readModel.ApplyTo(app, esClient, fetcher, parser, emitter, settings, logger);
    }

    runner.Initialize(RecurringTasks, fetcher, emitter, settings, logger);


    await TryActivateAdmin(fetcher, settings, emitter);

    foreach (var ingestor in Ingestors)
    {
      ingestor.ApplyTo(app, fetcher, emitter, settings);
    }

    foreach (var staticEndpoint in StaticEndpoints)
    {
      staticEndpoint.ApplyTo(app, fetcher, emitter, settings);
    }

    var hydrationDaemon = new ReadModelHydrationDaemon(
      settings,
      esClient,
      fetcher,
      parser,
      ReadModels.Where(rm => rm is IdempotentReadModel).Cast<IdempotentReadModel>().ToArray(),
      logger,
      interestFetcher);

    await hydrationDaemon.Initialize();

    processor = new TodoProcessor
    {
      Settings = settings,
      Fetcher = fetcher,
      Emitter = emitter,
      Tasks = Tasks.Concat(RecurringTasks.Select(rt => rt.ToTodoTaskDefinition())).ToArray(),
      ReadModels = ReadModels,
      Logger = logger,
      HydrationDaemon = hydrationDaemon
    };

    processor.Initialize();

    var dcbDaemon = new DynamicConsistencyBoundaryDaemon(esClient, parser, InterestTriggers, logger, interestFetcher);
    dcbDaemon.Initialize();

    CatchUp.Endpoint(ReadModels, hydrationDaemon, settings, fetcher, emitter, app);
    PreHydrationCompleted.Endpoint(ReadModels, hydrationDaemon, settings, fetcher, emitter, app);
    DaemonsInsight.Endpoint(
      ReadModels,
      settings,
      esClient,
      fetcher,
      emitter,
      app,
      hydrationDaemon,
      processor,
      dcbDaemon,
      projectionDaemon,
      logger);

    return fetcher;

    static async Task TryActivateAdmin(Fetcher fetcher, GeneratorSettings settings, Emitter emitter)
    {
      if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.Projections))
      {
        return;
      }

      await (
          from admin in fetcher
            .Fetch<UserSecurity>(new StrongString(settings.AdminId))
            .Map<FetchResult<UserSecurity>, Result<UserSecurity, ApiError>>(fr =>
              Ok(fr.Ent.DefaultValue(UserSecurity.Defaulted(new StrongString(settings.AdminId))))
            )
            .Async()
          from success in Activate(admin)
          select success)
        .Match(Id, _ => throw new InvalidOperationException("Could not activate admin"));
      return;

      AsyncResult<Unit, ApiError> Activate(UserSecurity admin) =>
        admin.ApplicationPermissions.Any(r => r == "admin")
          ? unit
          : emitter
            .Emit(() => new AnyState(new ApplicationPermissionAssigned(settings.AdminId, "admin")))
            .Async()
            .Map(_ => unit);
    }
  }

  private static Func<ResolvedEvent, Option<EventModelEvent>> Compose(
    params (string eventTypeName, Func<ResolvedEvent, Option<EventModelEvent>> parser)[] parsers
  )
  {
    var parsersDictionary = parsers.ToDictionary(tpl => tpl.eventTypeName, tpl => tpl.parser);
    return re => parsersDictionary.TryGetValue(re.Event.EventType, out var parser) ? parser(re) : None;
  }

  private static Func<ResolvedEvent, Option<EventModelEvent>> Parser()
  {
    return AllEventModelEventShapes().Select(ParserBuilder.Build).ToArray().Apply(Compose);

    static IEnumerable<Type> AllEventModelEventShapes()
    {
      var assemblies = AppDomain.CurrentDomain.GetAssemblies();
      var result = new HashSet<Type>();

      foreach (var assembly in assemblies)
      {
        // There is a bug with the test runner that prevents loading some types
        // from system data while running tests.
        if (assembly.FullName?.StartsWith("System.Data.") ?? false)
        {
          continue;
        }

        var types = assembly.GetTypes().Where(t => t.GetInterfaces().Contains(typeof(EventModelEvent)) && t.IsClass);

        foreach (var type in types)
        {
          result.Add(type);
        }
      }

      return result;
    }
  }

  public override int GetHashCode()
  {
    var hash = new HashCode();
    foreach (var command in Commands)
    {
      hash.Add(command.GetHashCode());
    }

    foreach (var readModel in ReadModels)
    {
      hash.Add(readModel.GetHashCode());
    }

    foreach (var projection in Projections)
    {
      hash.Add(projection.GetHashCode());
    }

    foreach (var task in Tasks)
    {
      hash.Add(task.GetHashCode());
    }

    foreach (var recurringTask in RecurringTasks)
    {
      hash.Add(recurringTask.GetHashCode());
    }

    foreach (var ingestor in Ingestors)
    {
      hash.Add(ingestor.GetType().Name.GetHashCode());
    }

    if (ApiName != null)
    {
      hash.Add(ApiName.GetHashCode());
    }

    if (ApiVersion != null)
    {
      hash.Add(ApiVersion.GetHashCode());
    }

    return hash.ToHashCode();
  }
}

public record EventWithMetadata<E>(
  E Event,
  Position Revision,
  Uuid EventId,
  EventMetadata Metadata) where E : EventModelEvent
{
  public EventWithMetadata<E2> As<E2>(E2 e) where E2 : EventModelEvent => new(e, Revision, EventId, Metadata);
}
