using System.Collections.Concurrent;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Framework;
using Nvx.ConsistentAPI.InternalTooling;
using Nvx.ConsistentAPI.Store.Events.Metadata;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI;

internal class ReadModelHydrationDaemon(
  GeneratorSettings settings,
  EventStore<EventModelEvent> store,
  Fetcher fetcher,
  IdempotentReadModel[] readModels,
  ILogger logger)
{
  private const int HydrationRetryLimit = 100;
  private static readonly ConcurrentDictionary<string, IdempotentReadModel[]> ModelsForEvent = new();
  private static readonly TimeSpan HydrationRetryDelay = TimeSpan.FromSeconds(10);
  private readonly string connectionString = settings.ReadModelConnectionString;

  private readonly DatabaseHandlerFactory databaseHandlerFactory =
    new(settings.ReadModelConnectionString, logger);

  private readonly InterestFetcher interestFetcher = new(store);

  private readonly SemaphoreSlim lastPositionSemaphore = new(1);
  private readonly SemaphoreSlim semaphore = new(1);

  private readonly CentralHydrationStateMachine stateMachine = new(settings, logger);
  private HydrationCountTracker? hydrationCountTracker;

  private bool isInitialized;
  private ulong? lastCheckpoint;
  private ulong? lastPosition;

  public async Task<HydrationDaemonInsights> Insights(ulong lastEventPosition)
  {
    var currentPosition = lastPosition ?? 0UL;
    var percentageComplete = lastEventPosition == 0
      ? 100m
      : Convert.ToDecimal(currentPosition) * 100m / Convert.ToDecimal(lastEventPosition);
    return new HydrationDaemonInsights(
      currentPosition,
      lastCheckpoint ?? 0,
      Math.Min(100m, percentageComplete),
      await stateMachine.EventsBeingProcessedCount());
  }

  public bool IsUpToDate(ulong? position) =>
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
      IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FailedReadModelHydrationWithId')
        BEGIN
          CREATE TABLE [FailedReadModelHydrationWithId]
          (
            [StreamName] NVARCHAR(255) NOT NULL,
            [EventId] NVARCHAR(255) NOT NULL,
            [EventType] NVARCHAR(255) NOT NULL,
            [HappenedAt] DATETIME2 NOT NULL,
            [RetryCount] INT NOT NULL DEFAULT 0,
            [ErrorMessage] NVARCHAR(MAX) NOT NULL,
            [NextRetryFrom] DATETIME2 NOT NULL,
            [SerializedId] NVARCHAR(MAX) NOT NULL,
            [StrongIdTypeNamespace] NVARCHAR(255) NOT NULL,
            [StrongIdTypeName] NVARCHAR(255) NOT NULL,
            [Swimlane] NVARCHAR(255) NOT NULL,
          )
        END
      """;

    await connection.ExecuteAsync(failedHydrationSql);
  }

  private async Task<GlobalPosition?> GetCheckpoint()
  {
    await using var connection = new SqlConnection(connectionString);
    const string sql = "SELECT [Checkpoint] FROM [CentralDaemonCheckpoint] ORDER BY [Checkpoint] DESC";
    var value = await connection.QueryFirstOrDefaultAsync<string?>(sql);
    return
      value is null
      || !GlobalPosition.TryParse(value, out var position)
        ? null
        : position;
  }

  private async Task UpdateLastPosition(ulong? pos)
  {
    await lastPositionSemaphore.WaitAsync();
    lastPosition = pos > lastPosition || lastPosition is null ? pos : lastPosition;
    lastPositionSemaphore.Release();
  }

  private async Task Hydrate()
  {
    while (settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration))
    {
      TryStartTracker();
      try
      {
        var checkpoint = await GetCheckpoint();
        var request = checkpoint is null
          ? SubscribeAllRequest.Start()
          : SubscribeAllRequest.After(checkpoint.Value.CommitPosition);

        await UpdateLastPosition(checkpoint?.CommitPosition);
        lastCheckpoint = lastPosition;

        await foreach (var message in store.Subscribe(request))
        {
          switch (message)
          {
            case ReadAllMessage<EventModelEvent>.AllEvent evt:
            {
              try
              {
                await stateMachine.Queue(evt.StrongId, evt.Metadata, evt.Event, TryProcess);
              }
              catch (Exception ex)
              {
                logger.LogError(
                  ex,
                  "Error hydrating read models, event type: {Event}, event id: {EventId}, stream name: {StreamName}, stream position: {StreamPosition}",
                  evt.Event.EventType,
                  evt.Metadata.EventId,
                  evt.Event.GetStreamName(),
                  evt.Metadata.StreamPosition);
              }

              break;
            }
            case ReadAllMessage<EventModelEvent>.Checkpoint(var pos):
            {
              await stateMachine.Checkpoint(pos, Checkpoint);
              lastCheckpoint = pos;
              break;
            }
            case ReadAllMessage<EventModelEvent>.CaughtUp:
            {
              if (lastPosition is { } pos)
              {
                await stateMachine.Checkpoint(pos, Checkpoint);
              }

              ClearTracker();
              break;
            }
            case ReadAllMessage<EventModelEvent>.FellBehind:
            {
              TryStartTracker();
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

    ClearTracker();
  }

  private void ClearTracker(HydrationCountTracker? replacement = null)
  {
    hydrationCountTracker?.Dispose();
    hydrationCountTracker = replacement;
  }

  private void TryStartTracker()
  {
    if (hydrationCountTracker is not null)
    {
      return;
    }

    StartTracker();
  }

  private void StartTracker() => ClearTracker(new HydrationCountTracker(readModels.Length));

  private async Task TryProcess(StrongId strongId, StoredEventMetadata metadata, EventModelEvent @event)
  {
    try
    {
      TryStartTracker();
      // Skip processing if the event is known to not be the last of the stream.
      if (fetcher
          .GetCachedStreamRevision(@event.GetStreamName(), strongId)
          .Match(cachedRevision => cachedRevision >= metadata.StreamPosition, () => false))
      {
        await UpdateLastPosition(metadata.GlobalPosition);
        return;
      }

      if (IsInterestEvent(@event))
      {
        await TryProcessInterestedEvent(@event);
        await UpdateLastPosition(metadata.GlobalPosition);
        return;
      }

      var interestedTask = TryProcessInterestedStreams(@event);

      var ableReadModels =
        ModelsForEvent.GetOrAdd(
          @event.EventType,
          _ => readModels.Where(rm => rm.CanProject(@event)).ToArray());

      if (ableReadModels.Length == 0)
      {
        await interestedTask;
        await UpdateLastPosition(metadata.GlobalPosition);
        return;
      }

      await fetcher
        .DaemonFetch(strongId, @event.GetStreamName())
        .Iter(async entity =>
        {
          await ableReadModels
            .Select<IdempotentReadModel, Func<Task<Unit>>>(rm =>
              async () =>
              {
                await rm.TryProcess(
                  entity,
                  databaseHandlerFactory,
                  strongId,
                  metadata.GlobalPosition,
                  logger);
                return unit;
              })
            .Parallel();
        });
      await interestedTask;
      await UpdateLastPosition(metadata.GlobalPosition);
    }
    catch (Exception ex)
    {
      try
      {
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
          """
          IF NOT EXISTS (
            SELECT 1 FROM [FailedReadModelHydrationWithId]
            WHERE [StreamName] = @StreamName AND [EventId] = @EventId
          )
          INSERT INTO [FailedReadModelHydrationWithId] (
            [StreamName],
            [EventId],
            [EventType],
            [HappenedAt], 
            [ErrorMessage], 
            [NextRetryFrom],
            [SerializedId],
            [StrongIdTypeNamespace],
            [StrongIdTypeName],
            [Swimlane]
          ) VALUES (
            @StreamName,
            @EventId,
            @EventType,
            @HappenedAt,
            @ErrorMessage,
            @NextRetryFrom,
            @SerializedId,
            @StrongIdTypeNamespace,
            @StrongIdTypeName,
            @Swimlane
          )
          """,
          new
          {
            StreamName = @event.GetStreamName(),
            metadata.EventId,
            @event.EventType,
            HappenedAt = DateTime.UtcNow,
            ErrorMessage = ex.Message,
            NextRetryFrom = DateTime.UtcNow,
            SerializedId = Serialization.Serialize(strongId),
            StrongIdTypeNamespace = strongId.GetType().Namespace ?? string.Empty,
            StrongIdTypeName = strongId.GetType().Name,
            Swimlane = @event.GetSwimLane()
          });
      }
      catch (Exception dbEx)
      {
        logger.LogError(
          dbEx,
          "Error logging failed read model hydration for event {EventId} on stream {StreamName}",
          metadata.EventId,
          @event.GetStreamName());
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
                 .Map(id1 => (id: id1, ie.InterestedEntityStreamName)),
               InterestedEntityHadInterestRemoved ie => ie
                 .InterestedEntityId.GetStrongId()
                 .Map(id2 => (id: id2, ie.InterestedEntityStreamName)),
               _ => None
             })
    {
      await TryProcessInterestedStream(tuple.InterestedEntityStreamName, tuple.id);
    }
  }

  private async Task TryProcessInterestedStreams(EventModelEvent @event) =>
    await interestFetcher
      .Concerned(@event.GetStreamName())
      .Async()
      .Match(c => c.InterestedStreams, () => [])
      .Map(ies =>
        ies
          .Select<(string name, Dictionary<string, string> id), Func<Task<Unit>>>(interestedStream => async () =>
            await interestedStream
              .id.GetStrongId()
              .Map(async strongId =>
              {
                await TryProcessInterestedStream(interestedStream.name, strongId);
                return unit;
              })
              .DefaultValue(unit.ToTask()))
          .Parallel());

  private async Task TryProcessInterestedStream(string streamName, StrongId entityId)
  {
    var ableReadModels = readModels.Where(rm => rm.CanProject(streamName)).ToArray();
    if (ableReadModels.Length == 0)
    {
      return;
    }

    await fetcher
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
          .Parallel();
      });
  }

  private async Task Checkpoint(ulong position)
  {
    var serialized = new GlobalPosition(position, position).ToString();
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
              [NextRetryFrom],
              [SerializedId],
              [StrongIdTypeNamespace],
              [StrongIdTypeName],
              [Swimlane]
            FROM [FailedReadModelHydrationWithId]
            WHERE [RetryCount] < @RetryLimit
            AND [NextRetryFrom] < GETUTCDATE()
            ORDER BY [RetryCount] ASC
            """,
            new
            {
              RetryLimit = HydrationRetryLimit
            })
          .Map(Enumerable.ToArray);

        if (failedHydrations.Length == 0)
        {
          await Task.Delay(2_500);
          continue;
        }

        foreach (var fh in failedHydrations)
        {
          const string increaseRetrySql =
            """
            UPDATE [FailedReadModelHydrationWithId]
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

          var idDictionary = new Dictionary<string, string>
          {
            { "StrongIdTypeName", fh.StrongIdTypeName },
            { "SerializedId", fh.SerializedId }
          };

          if (!string.IsNullOrWhiteSpace(fh.StrongIdTypeNamespace))
          {
            idDictionary.Add("StrongIdTypeNamespace", fh.StrongIdTypeNamespace);
          }

          foreach (var id in idDictionary.GetStrongId())
          {
            await foreach (var @event in store.Read(ReadStreamRequest.Backwards(fh.Swimlane, id)).Events().Take(1))
            {
              await TryProcess(@event.Id, @event.Metadata, @event.Event);
              await connection.ExecuteAsync(
                "DELETE FROM [FailedReadModelHydrationWithId] WHERE [EventId] = @EventId AND [StreamName] = @StreamName",
                new { fh.EventId, fh.StreamName });
            }
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
          [NextRetryFrom],
          [SerializedId],
          [StrongIdTypeNamespace],
          [StrongIdTypeName],
          [Swimlane]
        FROM [FailedReadModelHydrationWithId]
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
  DateTime NextRetryFrom,
  string SerializedId,
  string StrongIdTypeNamespace,
  string StrongIdTypeName,
  string Swimlane);
