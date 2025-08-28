using System.Collections.Concurrent;
using Dapper;
using EventStore.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.InternalTooling;

namespace Nvx.ConsistentAPI;

internal class ReadModelHydrationDaemon(
  GeneratorSettings settings,
  EventStoreClient client,
  Fetcher fetcher,
  Func<ResolvedEvent, Option<EventModelEvent>> parser,
  IdempotentReadModel[] readModels,
  ILogger logger)
{
  private const int HydrationRetryLimit = 100;
  private static readonly ConcurrentDictionary<string, IdempotentReadModel[]> ModelsForEvent = new();
  private static readonly TimeSpan HydrationRetryDelay = TimeSpan.FromSeconds(10);
  private readonly string connectionString = settings.ReadModelConnectionString;

  private readonly DatabaseHandlerFactory databaseHandlerFactory =
    new(settings.ReadModelConnectionString, logger);

  private readonly InterestFetcher interestFetcher = new(client, parser);

  private readonly SemaphoreSlim lastPositionSemaphore = new(1);
  private readonly SemaphoreSlim semaphore = new(1);

  private readonly CentralHydrationStateMachine stateMachine = new(settings, logger);
  private HydrationCountTracker? hydrationCountTracker;

  private bool isInitialized;
  private ulong? lastCheckpoint;
  private Position? lastPosition;
  internal FailedHydration[] FailedHydrations { get; private set; } = [];

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
      await stateMachine.EventsBeingProcessedCount());
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
      _ = Task.Run(RetryFailedHydrations);

      isInitialized = true;
    }
    finally
    {
      semaphore.Release();
    }
  }

  private async Task DoInitialize() => await CreateTable();

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

    const string failedHydrationSql =
      """
      IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FailedReadModelHydration')
        BEGIN
          CREATE TABLE [FailedReadModelHydration]
          (
            [StreamName] NVARCHAR(255) NOT NULL,
            [EventId] NVARCHAR(255) NOT NULL,
            [EventType] NVARCHAR(255) NOT NULL,
            [HappenedAt] DATETIME2 NOT NULL,
            [RetryCount] INT NOT NULL DEFAULT 0,
            [ErrorMessage] NVARCHAR(MAX) NOT NULL,
            [NextRetryFrom] DATETIME2 NOT NULL,
          )
        END
      """;

    await connection.ExecuteAsync(failedHydrationSql);
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
          if (fetcher
              .GetCachedStreamRevision(@event.GetStreamName(), @event.GetEntityId())
              .Match(cachedRevision => cachedRevision >= evt.Event.EventNumber.ToInt64(), () => false))
          {
            await UpdateLastPosition(evt.Event.Position);
            return;
          }

          if (IsInterestEvent(@event))
          {
            await TryProcessInterestedEvent(@event);
            await UpdateLastPosition(evt.Event.Position);
            return;
          }

          var concernedTask = TryProcessConcernedStreams(@event.GetStreamName());

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

          await fetcher
            .DaemonFetch(@event.GetEntityId(), @event.GetStreamName())
            .Iter(async entity =>
            {
              await ableReadModels
                .Select<IdempotentReadModel, Func<Task<Unit>>>(rm =>
                  async () =>
                  {
                    await rm.TryProcess(
                      entity,
                      databaseHandlerFactory,
                      @event.GetEntityId(),
                      evt.OriginalEvent.Position.ToString(),
                      logger);
                    await UpdateLastPosition(evt.Event.Position);
                    return unit;
                  })
                .Parallel(3);
            });
          await concernedTask;
          await UpdateLastPosition(evt.Event.Position);
        });
    }
    catch (Exception ex)
    {
      try
      {
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
          """
          IF NOT EXISTS (
            SELECT 1 FROM [FailedReadModelHydration]
            WHERE [StreamName] = @StreamName AND [EventId] = @EventId
          )
          INSERT INTO [FailedReadModelHydration] (
            [StreamName], [EventId], [EventType], [HappenedAt], [ErrorMessage], [NextRetryFrom]
          ) VALUES (
            @StreamName, @EventId, @EventType, @HappenedAt, @ErrorMessage, @NextRetryFrom
          )
          """,
          new
          {
            StreamName = evt.Event.EventStreamId,
            EventId = evt.Event.EventId.ToString(),
            evt.Event.EventType,
            HappenedAt = DateTime.UtcNow,
            ErrorMessage = ex.Message,
            NextRetryFrom = DateTime.UtcNow
          });
      }
      catch (Exception dbEx)
      {
        logger.LogError(
          dbEx,
          "Error logging failed read model hydration for event {EventId} on stream {StreamName}",
          evt.Event.EventId,
          evt.Event.EventStreamId);
        throw;
      }
    }
  }

  internal static bool IsInterestEvent(EventModelEvent @event) =>
    @event
      is InterestedEntityRegisteredInterest
      or InterestedEntityHadInterestRemoved
      or ConcernedEntityReceivedInterest
      or ConcernedEntityHadInterestRemoved;

  private async Task TryProcessInterestedEvent(EventModelEvent @event)
  {
    foreach (var tuple in @event switch
             {
               InterestedEntityRegisteredInterest ie => ie
                 .InterestedEntityId.GetStrongId()
                 .Map(id1 => (id: id1, ie.InterestedEntityStreamName, ie.ConcernedEntityStreamName)),
               InterestedEntityHadInterestRemoved ie => ie
                 .InterestedEntityId.GetStrongId()
                 .Map(id2 => (id: id2, ie.InterestedEntityStreamName, ie.ConcernedEntityStreamName)),
               _ => None
             })
    {
      await TryProcessInterestedStream(tuple.InterestedEntityStreamName, tuple.id);
      await TryProcessConcernedStreams(tuple.ConcernedEntityStreamName);
    }
  }

  private async Task TryProcessConcernedStreams(string streamName) =>
    await interestFetcher
      .Concerns(streamName)
      .Map(ies =>
        ies
          .Select<Concern, Func<Task<Unit>>>(interestedStream => async () =>
            await TryProcessInterestedStream(interestedStream.StreamName, interestedStream.Id))
          .Parallel(3));

  private async Task<Unit> TryProcessInterestedStream(string streamName, StrongId entityId)
  {
    var ableReadModels = readModels.Where(rm => rm.CanProject(streamName)).ToArray();
    if (ableReadModels.Length == 0)
    {
      return unit;
    }

    return await fetcher
      .DaemonFetch(entityId, streamName, true)
      .Iter(async entity =>
      {
        await ableReadModels
          .Select<IdempotentReadModel, Func<Task<Unit>>>(rm =>
            async () =>
            {
              await rm.TryProcess(
                entity,
                databaseHandlerFactory,
                entityId,
                null,
                logger);
              return unit;
            })
          .Parallel(3);
      });
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

  private async Task RetryFailedHydrations()
  {
    while (settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration))
    {
      try
      {
        await using var connection = new SqlConnection(connectionString);
        var failedHydrations = await connection
          .QueryAsync<FailedHydration>(
            """
            SELECT TOP 500
              [StreamName],
              [EventId],
              [EventType],
              [HappenedAt],
              [RetryCount],
              [ErrorMessage],
              [NextRetryFrom]
            FROM [FailedReadModelHydration]
            WHERE [RetryCount] < @RetryLimit
            AND [NextRetryFrom] < GETUTCDATE()
            ORDER BY [RetryCount] ASC
            """,
            new
            {
              RetryLimit = HydrationRetryLimit
            })
          .Map(Enumerable.ToArray);

        FailedHydrations = failedHydrations;

        if (failedHydrations.Length == 0)
        {
          await Task.Delay(2_500);
          continue;
        }

        foreach (var fh in failedHydrations)
        {
          const string increaseRetrySql =
            """
            UPDATE [FailedReadModelHydration]
            SET [RetryCount] = [RetryCount] + 1, [NextRetryFrom] = @NextRetryFrom
            WHERE [EventId] = @EventId AND [StreamName] = @StreamName
            """;

          await connection.ExecuteAsync(
            increaseRetrySql,
            new
            {
              fh.EventId,
              fh.StreamName,
              NextRetryFrom = DateTime.UtcNow + HydrationRetryDelay * fh.RetryCount
            });

          // Starts on 0, retry up to HydrationRetryLimit - 1 times.
          if (fh.RetryCount >= HydrationRetryLimit - 1)
          {
            logger.LogCritical(
              "Event {EventId} on stream {StreamName} has failed to hydrate {RetryCount} times, giving up",
              fh.EventId,
              fh.StreamName,
              fh.RetryCount);
            continue;
          }

          var streamRead = client.ReadStreamAsync(Direction.Backwards, fh.StreamName, StreamPosition.End, 1);
          if (await streamRead.ReadState == ReadState.StreamNotFound)
          {
            continue;
          }

          await foreach (var resolvedEvent in streamRead.Take(1))
          {
            await TryProcess(resolvedEvent);
            await connection.ExecuteAsync(
              "DELETE FROM [FailedReadModelHydration] WHERE [EventId] = @EventId AND [StreamName] = @StreamName",
              new { fh.EventId, fh.StreamName });
          }
        }
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error retrying failed read model hydrations");
        await Task.Delay(500);
      }
    }
  }

  internal async Task<FailedHydration[]> GetLingeringFailedHydrations()
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration))
    {
      return [];
    }

    await using var connection = new SqlConnection(connectionString);
    return await connection
      .QueryAsync<FailedHydration>(
        """
        SELECT
          [StreamName],
          [EventId],
          [EventType],
          [HappenedAt],
          [RetryCount],
          [ErrorMessage],
          [NextRetryFrom]
        FROM [FailedReadModelHydration]
        WHERE [RetryCount] > 10
        ORDER BY [RetryCount] ASC
        """)
      .Map(Enumerable.ToArray);
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
