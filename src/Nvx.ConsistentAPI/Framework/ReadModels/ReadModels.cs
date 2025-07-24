using EventStore.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.InternalTooling;
using Nvx.ConsistentAPI.Store.Events;

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

  public bool IsUpToDate(Position? position) => isUpToDate;

  public Task<SingleReadModelInsights> Insights(ulong lastEventPosition, EventStoreClient eventStoreClien)
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

    await Initialize(esClient, fetcher, parser, databaseHandler, settings, logger);
  }

  public Type ShapeType { get; } = typeof(Shape);

  public async Task TryProcess(
    FoundEntity foundEntity,
    DatabaseHandlerFactory dbFactory,
    StrongId entityId,
    string? checkpoint,
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
    EventStoreClient client,
    Fetcher fetcher,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    DatabaseHandler<Shape> databaseHandler,
    ILogger logger)
  {
    reset = async () =>
    {
      await databaseHandler.Reset(false);
      isUpToDate = false;
      _ = Task.Run(() => Subscribe(client, fetcher, parser, databaseHandler, logger));
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
        lastProcessedEventPosition = lastCheckpointPosition =
          checkpoint == Position.Start ? null : checkpoint.CommitPosition;

        var read = client.ReadAllAsync(
          Direction.Backwards,
          checkpoint == Position.Start ? Position.End : checkpoint,
          StreamFilter.Prefix(StreamPrefix, $"{InterestedEntityEntity.StreamPrefix}{StreamPrefix}"));

        await foreach (var message in read.Messages)
        {
          switch (message)
          {
            case StreamMessage.Event(var evt):
            {
              if (streams.TryGetValue(evt.Event.EventStreamId, out var _))
              {
                lastProcessedEventPosition = evt.Event.Position.CommitPosition;
                continue;
              }

              await Handle(evt);
              lastProcessedEventPosition = evt.Event.Position.CommitPosition;

              streams[evt.Event.EventStreamId] = DateTime.UtcNow;
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
            case StreamMessage.AllStreamCheckpointReached(var pos):
            {
              await databaseHandler.UpdateCheckpoint(FromAll.After(pos).ToString());
              lastCheckpointPosition = pos.CommitPosition;
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

    async Task Handle(ResolvedEvent re)
    {
      var attemptsMade = 0;
      while (true)
      {
        try
        {
          await parser(re)
            .Match(
              async me =>
              {
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
                            re.Event.Position.ToString(),
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
                    .Match(r => r >= re.Event.EventNumber.ToInt64(), () => false))
                {
                  return;
                }

                await UpdateReadModel(
                  me.GetEntityId(),
                  re.Event.Position.ToString(),
                  true,
                  fetcher,
                  databaseHandler,
                  logger);
              },
              () => Task.CompletedTask);
          break;
        }
        catch (Exception ex)
        {
          if (15 < attemptsMade)
          {
            logger.LogError(ex, "Error handling {ShapeType} update {OriginalStreamId}", ShapeType, re.OriginalStreamId);
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
    EventStoreClient client,
    Fetcher fetcher,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    DatabaseHandler<Shape> databaseHandler,
    GeneratorSettings settings,
    ILogger logger)
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration))
    {
      return;
    }

    await databaseHandler.Initialize();
    _ = Task.Run(() => Subscribe(client, fetcher, parser, databaseHandler, logger));
  }

  private async Task UpdateReadModel(
    StrongId id,
    string? checkpoint,
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
      holder.Etag = IdempotentUuid.Generate(checkpoint ?? Guid.NewGuid().ToString()).ToString();
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
  Option<Position> GlobalPosition)
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

internal record ReadModelSyncState(FromAll LastPosition, DateTime LastSync, bool HasReachedEndOnce);

internal static class ReadModelProgress
{
  internal static decimal InventerPercentageProgress(decimal current, decimal last) =>
    current == 0m || last == 0m
      ? 100m
      : 100m - current / last * 100m;
}
