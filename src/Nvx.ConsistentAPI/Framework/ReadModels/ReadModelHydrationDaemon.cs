using System.Collections.Concurrent;
using Dapper;
using EventStore.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Framework.DaemonCoordination;
using Nvx.ConsistentAPI.InternalTooling;

namespace Nvx.ConsistentAPI;

internal class ReadModelHydrationDaemon(
  GeneratorSettings settings,
  EventStoreClient client,
  Fetcher fetcher,
  Func<ResolvedEvent, Option<EventModelEvent>> parser,
  IdempotentReadModel[] readModels,
  ILogger logger,
  InterestFetcher interestFetcher,
  MessageHub messageHub,
  string modelHash)
{
  private const int InterestParallelism = 6;

  // To be deprecated once the swarm model is 100% active.
  private const string CreateCheckpointTableSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CentralDaemonCheckpoint')
      BEGIN
        CREATE TABLE [CentralDaemonCheckpoint]
        (
         [Checkpoint] NVARCHAR(255) NOT NULL
        )
      END 
    """;

  private const string CreateHashedCheckpointTableSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CentralDaemonHashedCheckpoints')
      BEGIN
        CREATE TABLE [CentralDaemonHashedCheckpoints]
        (
         [ModelHash] NVARCHAR(255) NOT NULL,
         [Checkpoint] NUMERIC(20,0) NOT NULL,
         [LastUpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
        )
      END 
    """;

  private const string GetLegacyCheckpointSql =
    "SELECT [Checkpoint] FROM [CentralDaemonCheckpoint] ORDER BY [Checkpoint] DESC";

  private static readonly ConcurrentDictionary<string, IdempotentReadModel[]> ModelsForEvent = new();
  private readonly string connectionString = settings.ReadModelConnectionString;

  private readonly SemaphoreSlim lastPositionSemaphore = new(1);
  private readonly SemaphoreSlim semaphore = new(1);

  private readonly CentralHydrationStateMachine stateMachine = new(settings, logger);

  private readonly HydrationDaemonWorker[] workers = Enumerable
    .Range(1, settings.ParallelHydration)
    .Select(_ => new HydrationDaemonWorker(
      modelHash,
      settings.ReadModelConnectionString,
      fetcher,
      readModels,
      new DatabaseHandlerFactory(settings.ReadModelConnectionString, logger),
      messageHub,
      logger))
    .ToArray();

  private HydrationCountTracker? hydrationCountTracker;

  private bool isInitialized;
  private bool hasCaughtUp;
  private ulong? lastCheckpoint;
  private Position? lastPosition;

  public async Task<HydrationDaemonInsights> Insights(ulong lastEventPosition)
  {
    var currentPosition = lastPosition?.CommitPosition ?? 0UL;
    var percentageComplete = lastEventPosition == 0
      ? 100m
      : Convert.ToDecimal(currentPosition) * 100m / Convert.ToDecimal(lastEventPosition);
    return new HydrationDaemonInsights(
      currentPosition,
      lastCheckpoint ?? 0,
      Math.Min(100m, percentageComplete),
      await HydrationDaemonWorker.PendingEventsCount(modelHash, connectionString));
  }

  public async Task<bool> IsUpToDate(Position? position)
  {
    if (position is null && hasCaughtUp)
    {
      return await HydrationDaemonWorker.PendingEventsCount(modelHash, connectionString) == 0;
    }

    return hydrationCountTracker is null
           || (position.HasValue && lastPosition.HasValue && position.Value <= lastPosition.Value);
  }

  public bool HasReachedLive() =>
    hasCaughtUp;

  public async Task Initialize()
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration))
    {
      return;
    }

    try
    {
      if (isInitialized)
      {
        return;
      }

      await semaphore.WaitAsync();
      if (isInitialized)
      {
        return;
      }

      await DoInitialize();
      _ = Task.Run(Hydrate);
      TriggerWorkers();

      isInitialized = true;
    }
    finally
    {
      semaphore.Release();
    }
  }

  private async Task DoInitialize()
  {
    await CreateTable();
    await using var connection = new SqlConnection(connectionString);
    foreach (var sql in HydrationDaemonWorker.TableCreationScripts)
    {
      await connection.ExecuteAsync(sql);
    }

    foreach (var readModel in readModels)
    {
      await HydrationDaemonWorker.TryInitialLockReadModel(modelHash, readModel.TableName, connection);
    }
  }

  private async Task CreateTable()
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration))
    {
      return;
    }

    await using var connection = new SqlConnection(connectionString);
    await connection.ExecuteAsync(CreateCheckpointTableSql);
    await connection.ExecuteAsync(CreateHashedCheckpointTableSql);
  }

  private async Task<FromAll> GetCheckpoint()
  {
    await using var connection = new SqlConnection(connectionString);
    var forThisHash = await ForThisHash(connection);

    if (forThisHash is { } forThis)
    {
      return FromAll.After(new Position(forThis, forThis));
    }

    var forAnyHash = await ForAnyHash(connection);

    if (forAnyHash is { } forAny)
    {
      return FromAll.After(new Position(forAny, forAny));
    }

    return await FromLegacy(connection) is { } legacy ? FromAll.After(legacy) : FromAll.Start;

    async Task<Position?> FromLegacy(SqlConnection conn)
    {
      var value = await conn.QueryFirstOrDefaultAsync<string?>(GetLegacyCheckpointSql);
      return value is not null && Position.TryParse(value, out var position) && position != null ? position : null;
    }

    async Task<ulong?> ForThisHash(SqlConnection conn)
    {
      var value = await conn.QueryFirstOrDefaultAsync<decimal?>(
        """
        SELECT TOP 1 [Checkpoint]
        FROM [CentralDaemonHashedCheckpoints]
        WHERE [ModelHash] = @ModelHash
        ORDER BY [LastUpdatedAt] DESC
        """,
        new { ModelHash = modelHash });
      return value is { } val ? Convert.ToUInt64(val) : null;
    }

    async Task<ulong?> ForAnyHash(SqlConnection conn)
    {
      var value = await conn.QueryFirstOrDefaultAsync<decimal?>(
        "SELECT TOP 1 [Checkpoint] FROM [CentralDaemonHashedCheckpoints] ORDER BY [LastUpdatedAt] DESC");
      return value is { } val ? Convert.ToUInt64(val) : null;
    }
  }

  private async Task UpdateLastPosition(Position? pos)
  {
    await lastPositionSemaphore.WaitAsync();
    lastPosition = pos > lastPosition || lastPosition is null ? pos : lastPosition;
    lastPositionSemaphore.Release();
  }

  private async Task Hydrate()
  {
    hydrationCountTracker = null;
    while (settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration))
    {
      try
      {
        var checkpoint = await GetCheckpoint();
        Position? lastPositionCandidate = checkpoint == FromAll.Start
          ? null
          : new Position(checkpoint.ToUInt64().commitPosition, checkpoint.ToUInt64().preparePosition);
        await UpdateLastPosition(lastPositionCandidate);
        lastCheckpoint = lastPosition?.CommitPosition;
        StartTracker();

        await using var subscription = client.SubscribeToAll(
          checkpoint,
          filterOptions: new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents()));
        await foreach (var message in subscription.Messages)
        {
          switch (message)
          {
            case StreamMessage.Event(var evt):
            {
              try
              {
                await stateMachine.Queue(evt, TryProcess);
              }
              catch (Exception ex)
              {
                logger.LogError(
                  ex,
                  "Error hydrating read models, event type: {Event}, event id: {EventId}, stream name: {StreamName}, stream position: {StreamPosition}",
                  evt.Event.EventType,
                  evt.Event.EventId,
                  evt.Event.EventStreamId,
                  evt.OriginalEvent.EventNumber);
              }

              break;
            }
            case StreamMessage.AllStreamCheckpointReached(var pos):
            {
              await stateMachine.Checkpoint(pos, Checkpoint);
              lastCheckpoint = pos.CommitPosition;
              break;
            }
            case StreamMessage.CaughtUp:
            {
              hasCaughtUp = true;
              if (lastPosition is { } pos)
              {
                await stateMachine.Checkpoint(pos, Checkpoint);
              }

              ClearTracker();
              break;
            }
            case StreamMessage.FellBehind:
            {
              StartTracker();
              break;
            }
          }
        }
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error queuing read models hydration");
      }
    }

    return;

    void ClearTracker()
    {
      hydrationCountTracker?.Dispose();
      hydrationCountTracker = null;
    }

    void StartTracker()
    {
      ClearTracker();
      hydrationCountTracker = new HydrationCountTracker(readModels.Length);
    }
  }

  private async Task TryProcess(ResolvedEvent evt)
  {
    try
    {
      await parser(evt)
        .Async()
        .Iter(async @event =>
        {
          // Skip processing if the event is known to not be the last of the stream.
          var isMidStream = fetcher
            .GetCachedStreamRevision(@event.GetStreamName(), @event.GetEntityId())
            .Match(cachedRevision => cachedRevision > evt.Event.EventNumber.ToInt64(), () => false);
          var interestCachedRevision = interestFetcher.GetCachedRevision(@event.GetStreamName());
          var isMidInterest =
            interestCachedRevision is not null
            && interestCachedRevision > evt.Event.EventNumber.ToInt64();
          if (isMidStream || isMidInterest)
          {
            await UpdateLastPosition(evt.Event.Position);
            return;
          }

          if (IsInterestEvent(@event))
          {
            await TryProcessInterestedEvent(@event, evt.Event.Position.CommitPosition);
            await UpdateLastPosition(evt.Event.Position);
            return;
          }

          var concernedTask = TryProcessConcernedStreams(
            @event.GetStreamName(),
            evt.Event.Position.CommitPosition);

          var ableReadModels =
            ModelsForEvent.GetOrAdd(
              evt.Event.EventType,
              _ => readModels.Where(rm => rm.CanProject(@event)).ToArray());

          if (ableReadModels.Length == 0)
          {
            await concernedTask;
            await UpdateLastPosition(evt.Event.Position);
            return;
          }

          await HydrationDaemonWorker.Register(
            modelHash,
            settings.ReadModelConnectionString,
            evt.OriginalStreamId,
            @event.GetEntityId(),
            evt.Event.Position.CommitPosition,
            false,
            readModels);
          TriggerWorkers();

          await concernedTask;
          await UpdateLastPosition(evt.Event.Position);
        });
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to queue hydrations");
      await Task.Delay(250);
    }
  }

  private void TriggerWorkers() => messageHub.WakeUpHydrationWorkers();

  internal static bool IsInterestEvent(EventModelEvent @event) =>
    @event
      is InterestedEntityRegisteredInterest
      or InterestedEntityHadInterestRemoved
      or ConcernedEntityReceivedInterest
      or ConcernedEntityHadInterestRemoved;

  private async Task TryProcessInterestedEvent(EventModelEvent @event, ulong globalPosition)
  {
    var interested = @event switch
    {
      InterestedEntityRegisteredInterest ie => ie
        .InterestedEntityId.GetStrongId()
        .Map(id => (id, ie.InterestedEntityStreamName, ie.ConcernedEntityStreamName)),
      InterestedEntityHadInterestRemoved ie => ie
        .InterestedEntityId.GetStrongId()
        .Map(id => (id, ie.InterestedEntityStreamName, ie.ConcernedEntityStreamName)),
      _ => None
    };

    foreach (var tuple in interested)
    {
      // Skip processing if the event is known to not be the last of the joint stream.
      if (fetcher
          .GetCachedLastPosition(tuple.InterestedEntityStreamName, tuple.id)
          .Match(cachedRevision => cachedRevision > globalPosition, () => false))
      {
        continue;
      }

      await HydrationDaemonWorker.Register(
        modelHash,
        connectionString,
        tuple.InterestedEntityStreamName,
        tuple.id,
        globalPosition,
        true,
        readModels);
      TriggerWorkers();
    }

    var concerned = @event switch
    {
      ConcernedEntityReceivedInterest ce => ce
        .ConcernedEntityId.GetStrongId()
        .Map(_ => ce.ConcernedEntityStreamName),
      ConcernedEntityHadInterestRemoved ce => ce
        .ConcernedEntityId.GetStrongId()
        .Map(_ => ce.ConcernedEntityStreamName),
      _ => None
    };

    foreach (var concernedEntityStreamName in concerned)
    {
      await TryProcessConcernedStreams(concernedEntityStreamName, globalPosition);
    }
  }

  private async Task TryProcessConcernedStreams(string streamName, ulong globalPosition) =>
    await interestFetcher
      .Concerns(streamName)
      .Map(ies =>
        ies
          .Select<Concern, Func<Task<Unit>>>(interestedStream => async () =>
            await TryProcessInterestedStream(
              interestedStream.StreamName,
              interestedStream.Id,
              globalPosition))
          .Parallel(InterestParallelism));

  private async Task<Unit> TryProcessInterestedStream(
    string streamName,
    StrongId entityId,
    ulong globalPosition)
  {
    // Skip processing if the event is known to not be the last of the joint stream.
    if (fetcher
        .GetCachedLastPosition(streamName, entityId)
        .Match(cachedRevision => cachedRevision > globalPosition, () => false))
    {
      return unit;
    }

    await HydrationDaemonWorker.Register(
      modelHash,
      connectionString,
      streamName,
      entityId,
      globalPosition,
      true,
      readModels);
    TriggerWorkers();
    return unit;
  }

  private async Task Checkpoint(Position position)
  {
    var serialized = position.ToString();
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    try
    {
      await connection.ExecuteAsync(
        "INSERT INTO [CentralDaemonHashedCheckpoints] ([ModelHash], [Checkpoint]) VALUES (@ModelHash, @Checkpoint)",
        new { ModelHash = modelHash, Checkpoint = serialized },
        transaction);
      await connection.ExecuteAsync(
        "DELETE FROM [CentralDaemonHashedCheckpoints] WHERE [ModelHash] = @ModelHash AND [Checkpoint] != @Checkpoint",
        new { ModelHash = modelHash, Checkpoint = serialized },
        transaction);
      await transaction.CommitAsync();
      await UpdateLastPosition(position);
    }
    catch
    {
      // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
      if (transaction is not null)
      {
        await transaction.RollbackAsync();
      }
    }
  }
}
