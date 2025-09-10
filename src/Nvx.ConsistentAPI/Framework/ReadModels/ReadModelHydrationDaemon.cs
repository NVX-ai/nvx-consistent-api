using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using EventStore.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.InternalTooling;

namespace Nvx.ConsistentAPI;

internal class ReadModelHydrationDaemon
{
  private const int HydrationRetryLimit = 100;
  private const int InterestParallelism = 6;
  private static readonly ConcurrentDictionary<string, IdempotentReadModel[]> ModelsForEvent = new();
  private readonly EventStoreClient client;
  private readonly string connectionString;

  private readonly Fetcher fetcher;
  private readonly InterestFetcher interestFetcher;

  private readonly SemaphoreSlim lastPositionSemaphore = new(1);
  private readonly ILogger logger;
  private readonly string modelHash;
  private readonly Func<ResolvedEvent, Option<EventModelEvent>> parser;
  private readonly IdempotentReadModel[] readModels;
  private readonly SemaphoreSlim semaphore = new(1);
  private readonly GeneratorSettings settings;

  private readonly CentralHydrationStateMachine stateMachine;

  private readonly HydrationDaemonWorker[] workers;
  private HydrationCountTracker? hydrationCountTracker;

  private bool isInitialized;
  private ulong? lastCheckpoint;
  private Position? lastPosition;

  public ReadModelHydrationDaemon(
    GeneratorSettings settings,
    EventStoreClient client,
    Fetcher fetcher,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    IdempotentReadModel[] readModels,
    ILogger logger,
    InterestFetcher interestFetcher)
  {
    this.settings = settings;
    this.client = client;
    this.fetcher = fetcher;
    this.parser = parser;
    this.readModels = readModels;
    this.logger = logger;
    this.interestFetcher = interestFetcher;
    connectionString = settings.ReadModelConnectionString;
    stateMachine = new CentralHydrationStateMachine(settings, logger);

    modelHash = Convert.ToBase64String(
      SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(string.Empty, readModels.Select(rm => rm.TableName))))
    );

    workers = Enumerable
      .Range(1, settings.ParallelHydration)
      .Select(_ => new HydrationDaemonWorker(
        modelHash,
        settings.ReadModelConnectionString,
        fetcher,
        readModels,
        new DatabaseHandlerFactory(settings.ReadModelConnectionString, logger),
        logger))
      .ToArray();
  }

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

  public bool IsUpToDate(Position? position) =>
    hydrationCountTracker is null
    || (position.HasValue && lastPosition.HasValue && position.Value <= lastPosition.Value);

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
    await connection.ExecuteAsync(HydrationDaemonWorker.QueueTableSql);
  }

  private async Task CreateTable()
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration))
    {
      return;
    }

    await using var connection = new SqlConnection(connectionString);
    const string sql =
      """
      IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CentralDaemonCheckpoint')
        BEGIN
          CREATE TABLE [CentralDaemonCheckpoint]
          (
           [Checkpoint] NVARCHAR(255) NOT NULL
          )
        END 
      """;
    await connection.ExecuteAsync(sql);
  }

  private async Task<FromAll> GetCheckpoint()
  {
    await using var connection = new SqlConnection(connectionString);
    const string sql = "SELECT [Checkpoint] FROM [CentralDaemonCheckpoint] ORDER BY [Checkpoint] DESC";
    var value = await connection.QueryFirstOrDefaultAsync<string?>(sql);

    if (value is null)
    {
      return FromAll.Start;
    }

    return Position.TryParse(value, out var position) && position != null
      ? FromAll.After(position.Value)
      : FromAll.Start;
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
        logger.LogError(ex, "Error hydrating read models");
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
            await TryProcessInterestedEvent(@event, Convert.ToInt64(evt.Event.Position.CommitPosition));
            await UpdateLastPosition(evt.Event.Position);
            return;
          }

          var concernedTask = TryProcessConcernedStreams(
            @event.GetStreamName(),
            Convert.ToInt64(evt.Event.Position.CommitPosition));

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
            Convert.ToInt64(evt.Event.Position.CommitPosition),
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

  private void TriggerWorkers()
  {
    foreach (var worker in workers)
    {
      worker.Trigger();
    }
  }

  internal static bool IsInterestEvent(EventModelEvent @event) =>
    @event
      is InterestedEntityRegisteredInterest
      or InterestedEntityHadInterestRemoved
      or ConcernedEntityReceivedInterest
      or ConcernedEntityHadInterestRemoved;

  private async Task TryProcessInterestedEvent(EventModelEvent @event, long position)
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
      await HydrationDaemonWorker.Register(
        modelHash,
        connectionString,
        tuple.InterestedEntityStreamName,
        tuple.id,
        position,
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
      await TryProcessConcernedStreams(concernedEntityStreamName, position);
    }
  }

  private async Task TryProcessConcernedStreams(string streamName, long position) =>
    await interestFetcher
      .Concerns(streamName)
      .Map(ies =>
        ies
          .Select<Concern, Func<Task<Unit>>>(interestedStream => async () =>
            await TryProcessInterestedStream(interestedStream.StreamName, interestedStream.Id, position))
          .Parallel(InterestParallelism));

  private async Task<Unit> TryProcessInterestedStream(string streamName, StrongId entityId, long position)
  {
    var ableReadModels = readModels.Where(rm => rm.CanProject(streamName)).ToArray();
    if (ableReadModels.Length == 0)
    {
      return unit;
    }

    await HydrationDaemonWorker.Register(
      modelHash,
      connectionString,
      streamName,
      entityId,
      position,
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
        "INSERT INTO [CentralDaemonCheckpoint] ([Checkpoint]) VALUES (@Checkpoint)",
        new { Checkpoint = serialized },
        transaction);
      await connection.ExecuteAsync(
        "DELETE FROM [CentralDaemonCheckpoint] WHERE [Checkpoint] != @Checkpoint",
        new { Checkpoint = serialized },
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

public record FailedHydration(
  string StreamName,
  string EventId,
  string EventType,
  DateTime HappenedAt,
  int RetryCount,
  string? ErrorMessage,
  DateTime NextRetryFrom);
