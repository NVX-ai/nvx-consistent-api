using EventStore.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.InternalTooling;
using Nvx.ConsistentAPI.Store.Events;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI;

public interface HasId
{
  string Id { get; }
}

public interface EventModelReadModel : HasId
{
  StrongId GetStrongId();
}

public enum SortDirection
{
  Ascending,
  Descending
}

public interface UserBound
{
  string UserSub { get; }
}

public record SortBy(string Field, SortDirection Direction);

public delegate Option<T> ReadModelDefaulter<T>(string id, Option<UserSecurity> user, Option<Guid> tenantId);

public delegate bool ShouldHydrate<T>(T entity, bool isBackwards);

public class ReadModelDefinition<Shape, EntityShape> :
  EventModelingReadModelArtifact,
  IdempotentReadModel
  where Shape : EventModelReadModel
  where EntityShape : EventModelEntity<EntityShape>
{
  private readonly EtagHolder holder = new();
  private bool isUpToDate;
  private ulong? lastCheckpointPosition;
  private ulong? lastProcessedEventPosition;
  private Func<Task<Unit>> reset = () => unit.ToTask();
  public bool IsExposed { private get; init; } = true;
  public Action<OpenApiOperation> OpenApiCustomizer { private get; init; } = _ => { };
  public required string StreamPrefix { private get; init; }
  public required Func<EntityShape, Shape[]> Projector { private get; init; }
  public BuildCustomFilter CustomFilterBuilder { get; init; } = (_, _, _) => new CustomFilter(null, [], null);
  public required string AreaTag { private get; init; }
  public ReadModelDefaulter<Shape> Defaulter { get; init; } = (_, _, _) => None;
  public ShouldHydrate<EntityShape> ShouldHydrate { get; init; } = (_, _) => true;
  public AuthOptions Auth { get; init; } = new Everyone();

  public bool IsUpToDate(ulong? position) => isUpToDate;

  public Task<SingleReadModelInsights> Insights(ulong lastEventPosition, EventStore<EventModelEvent> store)
  {
    var currentPosition = lastProcessedEventPosition ?? lastCheckpointPosition ?? lastEventPosition;
    var percentageComplete = isUpToDate || lastEventPosition == 0
      ? 100m
      : ReadModelProgress.InventerPercentageProgress(
        Convert.ToDecimal(currentPosition),
        Convert.ToDecimal(lastEventPosition));

    return Task.FromResult(
      new SingleReadModelInsights(
        DatabaseHandler<Shape>.TableName(typeof(Shape)),
        lastProcessedEventPosition,
        lastCheckpointPosition,
        false,
        Math.Min(100, percentageComplete)));
  }

  public async Task ApplyTo(
    WebApplication app,
    EventStoreClient esClient,
    EventStore<EventModelEvent> store,
    Fetcher fetcher,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    Emitter emitter,
    GeneratorSettings settings,
    ILogger logger)
  {
    var factory = new DatabaseHandlerFactory(settings.ReadModelConnectionString, logger);
    var databaseHandler = factory.Get<Shape>();
    ReadModelRouteBuilder
      .Apply(
        fetcher,
        databaseHandler,
        emitter,
        settings,
        Auth,
        app,
        holder,
        OpenApiCustomizer,
        AreaTag,
        async () => await reset(),
        IsExposed,
        (user, id) => CustomFilterBuilder(user, id, factory),
        Defaulter,
        logger);

    await Initialize(store, fetcher, databaseHandler, settings, logger);
  }

  public Type ShapeType { get; } = typeof(Shape);

  public async Task TryProcess(
    FoundEntity foundEntity,
    DatabaseHandlerFactory dbFactory,
    StrongId entityId,
    ulong? checkpoint,
    ILogger logger)
  {
    if (foundEntity is FoundEntity<EntityShape> thisEntity)
    {
      await UpdateReadModel(
        entityId,
        checkpoint,
        false,
        thisEntity,
        dbFactory.Get<Shape>(),
        logger);
    }
  }

  public bool CanProject(EventModelEvent e) => e.GetStreamName().StartsWith(StreamPrefix);
  public bool CanProject(string streamName) => streamName.StartsWith(StreamPrefix);

  private async Task Subscribe(
    EventStore<EventModelEvent> store,
    Fetcher fetcher,
    DatabaseHandler<Shape> databaseHandler,
    ILogger logger)
  {
    reset = async () =>
    {
      await databaseHandler.Reset(false);
      isUpToDate = false;
      _ = Task.Run(() => Subscribe(store, fetcher, databaseHandler, logger));
      return unit;
    };

    try
    {
      isUpToDate = await databaseHandler.IsUpToDate();
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error checking if {ShapeType} is up to date", ShapeType);
    }

    if (!isUpToDate)
    {
      logger.LogInformation("Read model {ShapeType} is not up to date, catching up...", ShapeType);
    }

    const int streamCacheSize = 500_000;
    var streams = new Dictionary<string, DateTime>();
    while (!isUpToDate)
    {
      try
      {
        // ReSharper disable once ExplicitCallerInfoArgument
        using var activity = PrometheusMetrics.Source.StartActivity("ReadModelHydration");
        activity?.SetTag("read-model.hydration.name", ShapeType.Name);
        activity?.SetTag("read-model.hydration.kind", "direct");
        using var _ = new HydrationCountTracker();
        var checkpoint = await databaseHandler.Checkpoint();
        lastProcessedEventPosition = lastCheckpointPosition = checkpoint;

        string[] swimlanes = [StreamPrefix, $"{InterestedEntityEntity.StreamPrefix}{StreamPrefix}"];
        var request = checkpoint.HasValue
          ? ReadAllRequest.Before(
            checkpoint.Value,
            swimlanes)
          : ReadAllRequest.End(swimlanes);

        await foreach (var message in store.Read(request))
        {
          switch (message)
          {
            case ReadAllMessage<EventModelEvent>.AllEvent evt:
            {
              if (streams.TryGetValue(evt.Event.GetStreamName(), out var _))
              {
                lastProcessedEventPosition = evt.Metadata.GlobalPosition;
                continue;
              }

              await Handle(evt);
              lastProcessedEventPosition = evt.Metadata.GlobalPosition;

              streams[evt.Event.GetStreamName()] = DateTime.UtcNow;
              if (streams.Count > streamCacheSize - 1)
              {
                foreach (var key in streams
                           .OrderBy(kvp => kvp.Value)
                           .Take(streamCacheSize / 2)
                           .Select(kvp => kvp.Key)
                           .ToList())
                {
                  streams.Remove(key);
                }
              }

              break;
            }
            case ReadAllMessage<EventModelEvent>.Checkpoint(var pos):
            {
              await databaseHandler.UpdateCheckpoint(pos);
              lastCheckpointPosition = pos;
              break;
            }
          }
        }

        isUpToDate = true;
        await databaseHandler.MarkAsUpToDate();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error subscribing to {StreamPrefix}", StreamPrefix);
        await Task.Delay(250);
      }
    }

    return;

    async Task Handle(ReadAllMessage<EventModelEvent>.AllEvent ae)
    {
      var attemptsMade = 0;
      while (true)
      {
        try
        {
          var me = ae.Event;
          if (ReadModelHydrationDaemon.IsInterestEvent(me))
          {
            foreach (var t in me switch
                     {
                       InterestedEntityRegisteredInterest ie => ie
                         .InterestedEntityId.GetStrongId()
                         .Map(id => (id, ie.InterestedEntityStreamName)),
                       InterestedEntityHadInterestRemoved ie => ie
                         .InterestedEntityId.GetStrongId()
                         .Map(id => (id, ie.InterestedEntityStreamName)),
                       _ => None
                     })
            {
              await fetcher
                .DaemonFetch(t.id, t.InterestedEntityStreamName)
                .Iter(async entity =>
                {
                  if (entity is FoundEntity<EntityShape> foundEntity)
                  {
                    await UpdateReadModel(
                      t.id,
                      ae.Metadata.GlobalPosition,
                      false,
                      foundEntity,
                      databaseHandler,
                      logger);
                  }
                });
            }

            return;
          }

          if (fetcher
              .GetCachedStreamRevision(me.GetEntityId())
              .Match(r => r >= ae.Metadata.StreamPosition, () => false))
          {
            return;
          }

          await UpdateReadModel(
            me.GetEntityId(),
            ae.Metadata.GlobalPosition,
            true,
            fetcher,
            databaseHandler,
            logger);
          break;
        }
        catch (Exception ex)
        {
          if (15 < attemptsMade)
          {
            logger.LogError(
              ex,
              "Error handling {ShapeType} update {OriginalStreamId}",
              ShapeType,
              ae.Event.GetStreamName());
            throw;
          }

          await Task.Delay(250);
          attemptsMade++;
        }
      }
    }
    // ReSharper disable once FunctionNeverReturns
  }

  private async Task Initialize(
    EventStore<EventModelEvent> store,
    Fetcher fetcher,
    DatabaseHandler<Shape> databaseHandler,
    GeneratorSettings settings,
    ILogger logger)
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration))
    {
      return;
    }

    await databaseHandler.Initialize();
    _ = Task.Run(() => Subscribe(store, fetcher, databaseHandler, logger));
  }

  private async Task UpdateReadModel(
    StrongId id,
    ulong? checkpoint,
    bool isBackwards,
    Du<Fetcher, FoundEntity<EntityShape>> toProject,
    DatabaseHandler<Shape> databaseHandler,
    ILogger logger)
  {
    try
    {
      await
        toProject
          .Match<AsyncOption<FoundEntity<EntityShape>>>(
            fetcher => fetcher.Fetch<EntityShape>(Some(id)).Map(FoundEntity<EntityShape>.From).Async(),
            fe => fe)
          .Match(
            e => ShouldHydrate(e.Entity, isBackwards)
              ? databaseHandler.Update(
                Projector(e.Entity),
                checkpoint,
                new TraceabilityFields(
                  e.FirstEventAt,
                  e.LastEventAt,
                  e.FirstUserSubFound,
                  e.LastUserSubFound,
                  id.StreamId()),
                id)
              : unit.ToTask(),
            () => unit.ToTask());
      holder.Etag = IdempotentUuid.Generate(checkpoint?.ToString() ?? Guid.NewGuid().ToString()).ToString();
    }
    catch (Exception ex) when (!ex.Message.Contains("Cannot insert duplicate key in object"))
    {
      logger.LogError(ex, "Error updating {ShapeType} {Id}", ShapeType, id);
      throw;
    }
  }

  public override int GetHashCode() => Naming.ToSpinalCase<Shape>().GetHashCode();
}

public interface FoundEntity;

public record FoundEntity<T>(
  T Entity,
  DateTime FirstEventAt,
  DateTime LastEventAt,
  string? FirstUserSubFound,
  string? LastUserSubFound,
  long StreamRevision,
  Option<ulong> GlobalPosition)
  : FoundEntity
  where T : EventModelEntity<T>
{
  public static Option<FoundEntity<T>> From(FetchResult<T> fr) => fr
    .Ent.Bind(e => fr.GlobalPosition.Map(gp => (e, gp)))
    .Map(tuple => new FoundEntity<T>(
      tuple.e,
      fr.FirstEventAt ?? DateTime.UnixEpoch,
      fr.LastEventAt ?? fr.FirstEventAt ?? DateTime.UnixEpoch,
      fr.FirstUserSubFound,
      fr.LastUserSubFound,
      fr.Revision,
      tuple.gp));
}

internal record ReadModelSyncState(
  ulong LastPosition,
  DateTime LastSync,
  bool HasReachedEndOnce,
  bool IsBeingHydratedByAnotherInstance);

internal static class ReadModelProgress
{
  internal static decimal InventerPercentageProgress(decimal current, decimal last) =>
    current == 0m || last == 0m
      ? 100m
      : 100m - current / last * 100m;
}
