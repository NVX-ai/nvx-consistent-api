using Dapper;
using EventStore.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nvx.ConsistentAPI.InternalTooling;
using Nvx.ConsistentAPI.Metrics;

namespace Nvx.ConsistentAPI;

public interface TodoData;

public interface OverriddenScheduleTodo : TodoData
{
  DateTime ScheduledAt { get; }
}

public enum TodoOutcome
{
  Retry,
  Done,
  Locked
}

public interface TodoTaskDefinition
{
  string Type { get; }
  EventModelingProjectionArtifact Projection { get; }
  TimeSpan LockLength { get; }
  Type EntityIdType { get; }
  Type[] DependingReadModels { get; }

  Task<Du<EventInsertion, TodoOutcome>> Execute(
    string data,
    Fetcher fetcher,
    StrongId entityId,
    string connectionString,
    ILogger logger);
}

public interface ReadModelDetailsFactory
{
  TableDetails GetTableDetails<ReadModel>() where ReadModel : EventModelReadModel;
}

public class DatabaseHandlerFactory(string connectionString, ILogger logger) : ReadModelDetailsFactory
{
  public TableDetails GetTableDetails<ReadModel>() where ReadModel : EventModelReadModel
  {
    var handler = Get<ReadModel>();
    return new TableDetails(
      handler.GetTableName(),
      handler.UpsertSql,
      handler.TraceableUpsertSql,
      handler.GenerateSafeInsertSql(),
      handler.GenerateUpdateSql(),
      handler.AllColumns,
      new Dictionary<Type, AdditionalTableDetails>(),
      handler.AllColumnsTablePrefixed);
  }

  internal DatabaseHandler<ReadModel> Get<ReadModel>() where ReadModel : EventModelReadModel =>
    new(connectionString, logger);

  // Meant to be accessed by the TodoProcessor
  // ReSharper disable once UnusedMember.Global
  public async Task<IEnumerable<T>> Query<T>(Func<SqlConnection, Task<IEnumerable<T>>> query)
  {
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    return await query(connection);
  }

  // Meant to be accessed by the TodoProcessor
  // ReSharper disable once UnusedMember.Global
  public async Task<IEnumerable<ReadModel>> Query<ReadModel>(
    Func<TableDetails, string> query,
    object parameters) where ReadModel : EventModelReadModel
  {
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    return await connection.QueryAsync<ReadModel>(query(GetTableDetails<ReadModel>()), parameters);
  }
}

public class TodoTaskDefinition<DataShape, Entity, SourceEvent, EntityId> : TodoTaskDefinition
  where DataShape : TodoData
  where Entity : EventModelEntity<Entity>
  where SourceEvent : EventModelEvent
  where EntityId : StrongId
{
  public required
    Func<DataShape, Entity, Fetcher, DatabaseHandlerFactory, ILogger, Task<Du<EventInsertion, TodoOutcome>>> Action
  {
    get;
    init;
  }

  public required Func<SourceEvent, Entity, EventMetadata, TodoData> Originator { get; init; }
  public TimeSpan Delay { get; init; } = TimeSpan.Zero;
  public TimeSpan Expiration { get; init; } = TimeSpan.FromDays(7);
  public required string SourcePrefix { get; init; }
  public Type EntityIdType { get; } = typeof(EntityId);
  public Type[] DependingReadModels { get; init; } = [];
  public required string Type { get; init; }
  public TimeSpan LockLength { get; init; } = TimeSpan.FromHours(1);

  public async Task<Du<EventInsertion, TodoOutcome>> Execute(
    string data,
    Fetcher fetcher,
    StrongId entityId,
    string connectionString,
    ILogger logger)
  {
    try
    {
      return await DeserializeData(data, logger)
        .Async()
        .Bind(d => fetcher
          .Fetch<Entity>(entityId)
          .Map(fr => fr.Ent.Map(e => (e, fr.Revision)))
          .Async()
          .Map(e => (d, e.e, e.Revision)))
        .Map(async t =>
          await Action(
              t.d,
              t.e,
              fetcher,
              new DatabaseHandlerFactory(connectionString, logger),
              logger)
            .Map(du =>
              du.Match(
                ei => ei.WithRevision(t.Revision).Apply(First<EventInsertion, TodoOutcome>),
                Second<EventInsertion, TodoOutcome>))
        )
        .DefaultValue(TodoOutcome.Retry);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed executing todo:\n{ArgItem1}\nFor task {ArgItem2}", data, Type);
      return TodoOutcome.Retry;
    }
  }

  public EventModelingProjectionArtifact Projection =>
    new TodoProjector(SourcePrefix) { Delay = Delay, Expiration = Expiration, Originator = Originator, Type = Type };

  private Option<DataShape> DeserializeData(string data, ILogger logger)
  {
    try
    {
      return JsonConvert.DeserializeObject<DataShape>(data).Apply(Optional);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed deserializing todo data for task {TaskType}", Type);
      return None;
    }
  }

  private static Guid GetTaskId(Uuid sourceEventUuid, string type) =>
    IdempotentUuid.Generate($"{sourceEventUuid}{type})").ToGuid();

  public override int GetHashCode() => Type.GetHashCode();

  internal class TodoProjector : ProjectionDefinition<SourceEvent, TodoCreated, Entity, ProcessorEntity, StrongGuid>
  {
    private readonly string sourceStreamPrefix;

    internal TodoProjector(string sourceStreamPrefix)
    {
      this.sourceStreamPrefix = sourceStreamPrefix;
    }

    public required Func<SourceEvent, Entity, EventMetadata, TodoData> Originator { get; init; }
    public TimeSpan Delay { get; init; } = TimeSpan.Zero;
    public required string Type { get; init; }
    public TimeSpan Expiration { get; init; } = TimeSpan.FromDays(7);

    public override string Name => $"framework-todo-{Type}";

    // ReSharper disable once ConvertToAutoProperty
    public override string SourcePrefix => sourceStreamPrefix;

    public override Option<TodoCreated> Project(
      SourceEvent eventToProject,
      Entity e,
      Option<ProcessorEntity> projectionEntity,
      StrongGuid projectionId,
      Uuid sourceEventUuid,
      EventMetadata metadata)
    {
      var todoData = Originator(eventToProject, e, metadata);
      var startsAt = CalculateStartsAt();
      var expiresAt = startsAt + Expiration;
      return new TodoCreated(
        GetTaskId(sourceEventUuid, Type),
        startsAt,
        expiresAt,
        JsonConvert.SerializeObject(todoData),
        Type,
        eventToProject.GetEntityId().StreamId(),
        JsonConvert.SerializeObject(eventToProject.GetEntityId())
      );

      DateTime CalculateStartsAt() =>
        todoData is OverriddenScheduleTodo overriddenScheduleTodo
          ? overriddenScheduleTodo.ScheduledAt
          : metadata.CreatedAt + Delay;
    }

    public override IEnumerable<StrongGuid> GetProjectionIds(
      SourceEvent sourceEvent,
      Entity sourceEntity,
      Uuid sourceEventId) => [new(GetTaskId(sourceEventId, Type))];
  }
}

internal class TodoProcessor
{
  private static readonly Type UserWithTenantPermissionIdType = typeof(UserWithTenantPermissionId);
  private static readonly Type UserWithPermissionIdType = typeof(UserWithPermissionId);
  private static readonly Type StrongStringType = typeof(StrongString);
  private static readonly Type StrongGuidType = typeof(StrongGuid);

  private readonly string tableName =
    DatabaseHandler<TodoEventModelReadModel>.TableName(typeof(TodoEventModelReadModel));

  public required GeneratorSettings Settings { private get; init; }
  public required ILogger Logger { private get; init; }
  public required Fetcher Fetcher { private get; init; }
  public required Emitter Emitter { private get; init; }
  public required TodoTaskDefinition[] Tasks { private get; init; }
  public required ReadModelHydrationDaemon HydrationDaemon { private get; init; }
  internal required EventModelingReadModelArtifact[] ReadModels { get; init; }

  internal RunningTodoTaskInsight[] RunningTodoTasks { get; private set; } = [];

  internal async Task<RunningTodoTaskInsight[]> AboutToRunTasks()
  {
    var currentlyRunning = RunningTodoTasks;
    return await GetAboutToRunTodos()
      .Map(ts => ts
        .Choose(todoReadModel =>
          Tasks
            .FirstOrNone(t => t.Type == todoReadModel.Name)
            .Map(todoTaskDefinition => (todoTaskDefinition, todoReadModel))
        )
        .Where(t => currentlyRunning.All(rt =>
          rt.TaskType != t.todoTaskDefinition.Type
          && rt.RelatedEntityIds.All(id => id != t.todoReadModel.RelatedEntityId)))
        .GroupBy(t => t.todoTaskDefinition.Type)
        .Select(g => new RunningTodoTaskInsight(g.Key, g.Select(t => t.todoReadModel.RelatedEntityId).ToArray()))
        .ToArray());
  }

  internal void Initialize() => RunPeriodically(async () => await Process());

  private async Task Process()
  {
    try
    {
      var todos = await GetAvailableTodos();
      var matchedTodos = todos
        .Choose(todoReadModel =>
          Tasks
            .FirstOrNone(t => t.Type == todoReadModel.Name)
            .Map(todoTaskDefinition => (todoTaskDefinition, todoReadModel))
        )
        .ToArray();

      RunningTodoTasks = matchedTodos
        .GroupBy(t => t.todoTaskDefinition.Type)
        .Select(g => new RunningTodoTaskInsight(g.Key, g.Select(t => t.todoReadModel.RelatedEntityId).ToArray()))
        .ToArray();

      using var _ = new BatchTodoCountTracker(matchedTodos.Length);
      await matchedTodos.Select(ProcessOne).Parallel(10);
      if (matchedTodos.Length == 0)
      {
        await Task.Delay(250);
      }
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed processing todos");
    }
  }

  private Func<Task<Unit>> ProcessOne((TodoTaskDefinition definition, TodoEventModelReadModel todo) t) =>
    async () =>
    {
      using var activity = PrometheusMetrics.Source.StartActivity(nameof(TodoProcessor));
      using var _ = new RunningTodoCountTracker();
      try
      {
        // Await for all relevant read models to be up-to-date.
        if (t.definition.DependingReadModels.All(_ => ReadModels.All(rm => rm.IsUpToDate(t.todo.EventPosition)))
            && HydrationDaemon.IsUpToDate(t.todo.EventPosition))
        {
          return await TryFetch()
            .Option
            .Bind(fetchResult => fetchResult.Ent.Map(entity => (entity, fetchResult.Revision)))
            .Iter(async tuple =>
              await RequestLock(tuple.entity, tuple.Revision)
                .Bind(_ => Emitter.Emit(Decider).Async().Map(_ => unit))
                .Match(_ => Complete(), e => e.Match(ErrorApi, Outcome))
            );
        }

        return await TryFetch()
          .Match(fr => fr.Ent, _ => None)
          .Async()
          .Bind(e => e.LockState.Match(_ => Some(unit), _ => None))
          .Iter(_ =>
            Emitter
              .Emit(() =>
              {
                Logger.LogWarning("Todo {Todo} is waiting for read models to be up-to-date", t.todo);
                try
                {
                  // ReSharper disable once AccessToDisposedClosure
                  activity?.SetTag("todo.result", "waiting-read-models");
                }
                catch
                {
                  // ignore
                }

                return new AnyState(new TodoHadDependingReadModelBehind(Guid.Parse(t.todo.Id)));
              })
              .Async()
              .Match(_ => unit, _ => unit));
      }
      catch (Exception ex)
      {
        activity?.SetTag("todo.result", "failure");
        if (t.todo.RetryCount == ProcessorEntity.MaxAttempts - 1)
        {
          Logger.LogCritical(
            ex,
            @"Failed processing todo:\n{Todo}\nFor task {TaskType} after {RetryLimit} retries, it will not run again",
            t.todo,
            t.definition.Type,
            ProcessorEntity.MaxAttempts - 1);
        }
        else
        {
          Logger.LogError(
            ex,
            @"Failed processing todo:\n{Todo}\nFor task {TaskType}",
            t.todo,
            t.definition.Type);
        }
      }

      return unit;

      AsyncResult<T, Du<ApiError, TodoOutcome>> Elevate<T>(AsyncResult<T, TodoOutcome> ar) =>
        ar.MapError(Second<ApiError, TodoOutcome>);

      AsyncResult<Unit, Du<ApiError, TodoOutcome>> RequestLock(ProcessorEntity p, long revision) =>
        from available in p.LockState.Apply(Elevate)
        from locked in TryInsertLock(revision, available)
        select locked;

      AsyncResult<FetchResult<ProcessorEntity>, Du<ApiError, TodoOutcome>> TryFetch() =>
        Fetcher.Fetch<ProcessorEntity>(Some(t.todo.GetStrongId()));

      AsyncResult<Unit, Du<ApiError, TodoOutcome>> TryInsertLock(
        long revision,
        // ReSharper disable once UnusedParameter.Local
        // This is to ensure that this is called only after checking that locking is possible.
        ProcessorEntity.LockAvailable _
      ) =>
        Emitter
          .TryInsert(
            new TodoLockRequested(
              t.todo.Id.Apply(Guid.Parse),
              DateTime.UtcNow,
              t.definition.LockLength
            ),
            revision,
            null
          )
          .MapError(_ => TodoOutcome.Locked)
          .Apply(Elevate);

      Task<Unit> Complete()
      {
        var now = DateTime.UtcNow;
        return Emitter
          .Emit(() => new AnyState(new TodoCompleted(t.todo.Id.Apply(Guid.Parse), now)))
          .Async()
          .Match(_ => unit, _ => unit);
      }

      Task<Unit> Release() =>
        Emitter
          .Emit(() => new AnyState(new TodoLockReleased(t.todo.Id.Apply(Guid.Parse))))
          .Async()
          .Match(_ => unit, _ => unit);

      // This will retry, but not release the lock, adding some effective delay.
      Task<Unit> ErrorApi(ApiError ae) => Task.FromResult(unit);

      Task<Unit> Outcome(TodoOutcome outcome) =>
        outcome switch
        {
          TodoOutcome.Locked => unit.ToTask(),
          TodoOutcome.Done => Complete(),
          _ => Release()
        };

      AsyncResult<EventInsertion, Du<ApiError, TodoOutcome>> Decider()
      {
        return Go();

        async Task<Result<EventInsertion, Du<ApiError, TodoOutcome>>> Go()
        {
          // ReSharper disable once AccessToDisposedClosure
          activity?.SetTag("todo.id", t.todo.SerializedRelatedEntityId ?? t.todo.Id);
          // ReSharper disable once AccessToDisposedClosure
          activity?.SetTag("todo.name", t.todo.Name);

          var result = await t.definition
            .Execute(
              t.todo.JsonData,
              Fetcher,
              GetStrongId(),
              Settings.ReadModelConnectionString,
              Logger);

          result.Iter(
            // ReSharper disable once AccessToDisposedClosure
            _ => activity?.SetTag("todo.result", "success"),
            // ReSharper disable once AccessToDisposedClosure
            r => activity?.SetTag(
              "todo.result",
              r switch
              {
                TodoOutcome.Done => "success",
                TodoOutcome.Locked => "locked",
                TodoOutcome.Retry => "retry",
                _ => "unknown"
              }));

          return result.Match(
            Ok<EventInsertion, Du<ApiError, TodoOutcome>>,
            to => to.Apply(Second<ApiError, TodoOutcome>)
          );

          StrongId GetStrongId()
          {
            if (!string.IsNullOrEmpty(t.todo.SerializedRelatedEntityId))
            {
              try
              {
                return (StrongId)JsonConvert.DeserializeObject(
                  t.todo.SerializedRelatedEntityId!,
                  t.definition.EntityIdType)!;
              }
              catch (Exception ex)
              {
                Logger.LogError(
                  ex,
                  "Failed deserializing {SerializedRelatedEntityId}\nfor task {Todo}\nfor definition {Definition}",
                  t.todo.SerializedRelatedEntityId,
                  t.todo,
                  t.definition);
                return new StrongString(t.todo.RelatedEntityId);
              }
            }

            if (StrongGuidType == t.definition.EntityIdType)
            {
              return new StrongGuid(Guid.TryParse(t.todo.RelatedEntityId, out var id) ? id : Guid.NewGuid());
            }

            if (StrongStringType == t.definition.EntityIdType)
            {
              return new StrongString(t.todo.RelatedEntityId);
            }

            if (UserWithPermissionIdType == t.definition.EntityIdType)
            {
              try
              {
                return new UserWithPermissionId(
                  t.todo.RelatedEntityId.Split("#")[0],
                  t.todo.RelatedEntityId.Split("#")[1]);
              }
              catch (Exception ex)
              {
                Logger.LogError(
                  ex,
                  "Failed deserializing UserWithPermissionId: {RelatedEntityId}\nfor task {Todo}\nfor definition {Definition}",
                  t.todo.RelatedEntityId,
                  t.todo,
                  t.definition);
                return new StrongString(t.todo.RelatedEntityId);
              }
            }

            if (UserWithTenantPermissionIdType == t.definition.EntityIdType)
            {
              try
              {
                return new UserWithTenantPermissionId(
                  t.todo.RelatedEntityId.Split("#")[0],
                  Guid.Parse(t.todo.RelatedEntityId.Split("#")[1]),
                  t.todo.RelatedEntityId.Split("#")[2]
                );
              }
              catch (Exception ex)
              {
                Logger.LogError(
                  ex,
                  "Failed deserializing UserWithTenantPermissionId: {RelatedEntityId}\nfor task {Todo}\nfor definition {Definition}",
                  t.todo.RelatedEntityId,
                  t.todo,
                  t.definition);
              }
            }

            return new StrongString(t.todo.RelatedEntityId);
          }
        }
      }
    };

  private async Task<IEnumerable<TodoEventModelReadModel>> GetAboutToRunTodos()
  {
    try
    {
      const int batchSize = 2_500;
      var aMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
      var now = DateTime.UtcNow;

      var query = $"""
                      SELECT TOP (@BatchSize)
                          [Id],
                          [RelatedEntityId],
                          [StartsAt],
                          [ExpiresAt],
                          [JsonData],
                          [Name],
                          [LockedUntil],
                          [SerializedRelatedEntityId],
                          [EventPosition],
                          [RetryCount]
                      FROM [{tableName}]
                      WHERE
                          ([StartsAt] <= @aMinuteAgo) AND
                          [ExpiresAt] > @now AND
                          ([LockedUntil] IS NULL OR [LockedUntil] < @aMinuteAgo)
                      ORDER BY [StartsAt] ASC
                   """;

      await using var connection = new SqlConnection(Settings.ReadModelConnectionString);
      return await connection.QueryAsync<TodoEventModelReadModel>(
        query,
        new { BatchSize = batchSize, aMinuteAgo, now });
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed getting about to run todos");
      throw;
    }
  }

  private async Task<IEnumerable<TodoEventModelReadModel>> GetAvailableTodos()
  {
    try
    {
      const int batchSize = 2_500;
      var now = DateTime.UtcNow;

      var query = $"""
                              SELECT TOP (@BatchSize)
                                  [Id],
                                  [RelatedEntityId],
                                  [StartsAt],
                                  [ExpiresAt],
                                  [JsonData],
                                  [Name],
                                  [LockedUntil],
                                  [SerializedRelatedEntityId],
                                  [EventPosition],
                                  [RetryCount]
                              FROM [{tableName}]
                              WHERE
                                  [StartsAt] <= @now AND
                                  [ExpiresAt] > @now AND
                                  ([LockedUntil] IS NULL OR [LockedUntil] < @now)
                              ORDER BY [StartsAt] ASC
                   """;

      await using var connection = new SqlConnection(Settings.ReadModelConnectionString);
      return await connection.QueryAsync<TodoEventModelReadModel>(
        query,
        new { BatchSize = batchSize, now });
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed getting available todos");
      throw;
    }
  }

  private void RunPeriodically(Func<Task> taskFunc) =>
    Task.Run(async () =>
    {
      while (Settings.EnabledFeatures.HasFlag(FrameworkFeatures.Tasks))
      {
        try
        {
          await Task.Delay(500);
          await taskFunc();
        }
        catch (Exception ex)
        {
          Logger.LogError(ex, "Failure in the task running loop");
        }
      }
    });
}
