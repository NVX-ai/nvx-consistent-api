using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Framework;
using Nvx.ConsistentAPI.Framework.DaemonCoordination;

namespace Nvx.ConsistentAPI;

public class HydrationDaemonWorker
{
  private const string QueueTableCreationSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'HydrationQueue')
    BEGIN
    CREATE TABLE [dbo].[HydrationQueue](
        [StreamName] [nvarchar](3000) NOT NULL,
        [SerializedId] [nvarchar](max) NOT NULL,
        [IdTypeName] [nvarchar](256) NOT NULL,
        [IdTypeNamespace] [nvarchar](256) NULL,
        [ModelHash] [nvarchar](256) NOT NULL,
        [Position] NUMERIC(20,0) NOT NULL,
        [WorkerId] [uniqueidentifier] NULL,
        [LockedUntil] [datetime2](7) NULL,
        [TimesLocked] [int] NOT NULL,
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT (GETUTCDATE()),
        [IsDynamicConsistencyBoundary] [bit] NOT NULL DEFAULT (0),
        [LastHydratedPosition] NUMERIC(20,0) NULL DEFAULT NULL)
    END
    """;

  private const string GetCandidatesIndexCreationSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_HydrationQueue_GetCandidates')
    BEGIN
      CREATE NONCLUSTERED INDEX [IX_HydrationQueue_GetCandidates]
      ON [dbo].[HydrationQueue] ([ModelHash], [TimesLocked], [IsDynamicConsistencyBoundary], [Position])
      INCLUDE ([LockedUntil], [LastHydratedPosition]);
    END
    """;

  private const string TryLockIndexCreationSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_HydrationQueue_TryLock')
    BEGIN
      CREATE NONCLUSTERED INDEX [IX_HydrationQueue_TryLock]
      ON [dbo].[HydrationQueue] ([StreamName], [ModelHash], [Position], [TimesLocked])
      INCLUDE ([LockedUntil], [WorkerId]);
    END
    """;

  private const string ModelHashReadModelLockTableCreationSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ModelHashReadModelLocks')
    BEGIN
    CREATE TABLE [dbo].[ModelHashReadModelLocks](
        [ModelHash] [nvarchar](256) NOT NULL PRIMARY KEY,
        [ReadModelName] [nvarchar](256) NOT NULL,
        [LockedUntil] [datetime2](7) NOT NULL
    )
    END
    """;

  private const string GetCandidatesSql =
    """
    SELECT TOP 25 *
    FROM [HydrationQueue]
    WHERE ([LockedUntil] IS NULL OR [LockedUntil] < GETUTCDATE())
      AND [ModelHash] = @ModelHash
      AND [TimesLocked] < 25
      AND ([LastHydratedPosition] IS NULL OR [Position] > [LastHydratedPosition])
    ORDER BY [IsDynamicConsistencyBoundary], [Position] ASC
    """;

  private const int StreamLockLengthSeconds = 42;
  private const int RefreshStreamLockFrequencySeconds = StreamLockLengthSeconds / 3;

  private const string UpdateHydrationState =
    """
    UPDATE [HydrationQueue]
    SET [WorkerId] = NULL,
        [LockedUntil] = NULL,
        [LastHydratedPosition] = @LastHydratedPosition
    WHERE [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
      AND [WorkerId] = @WorkerId
    """;

  private const string UpsertSql =
    """
    MERGE [HydrationQueue] AS target
    USING (
      SELECT
        @StreamName AS StreamName,
        @ModelHash AS ModelHash,
        @Position AS Position,
        @SerializedId AS SerializedId,
        @IdTypeName AS IdTypeName,
        @IdTypeNamespace AS IdTypeNamespace,
        @IsDynamicConsistencyBoundary AS IsDynamicConsistencyBoundary
    ) AS source
    ON target.[StreamName] = source.StreamName
       AND target.[ModelHash] = source.ModelHash
    WHEN MATCHED THEN
        UPDATE SET 
          [TimesLocked] = 0,
          [Position] = source.Position,
          [IsDynamicConsistencyBoundary] =
            CASE 
              WHEN target.[IsDynamicConsistencyBoundary] = 1 
              THEN 1 
              ELSE source.IsDynamicConsistencyBoundary END
    WHEN NOT MATCHED THEN
        INSERT (
          [StreamName],
          [SerializedId],
          [IdTypeName],
          [IdTypeNamespace],
          [ModelHash],
          [Position],
          [TimesLocked],
          [IsDynamicConsistencyBoundary]
        )
        VALUES (
          source.StreamName,
          source.SerializedId,
          source.IdTypeName,
          source.IdTypeNamespace,
          source.ModelHash,
          source.Position,
          0,
          source.IsDynamicConsistencyBoundary
        );
    """;

  private const string PendingEventsCountSql =
    """
    SELECT COUNT(*)
    FROM [HydrationQueue]
    WHERE [ModelHash] = @ModelHash 
      AND [TimesLocked] < 25
      AND ([LastHydratedPosition] IS NULL OR [Position] > [LastHydratedPosition])";
    """;

  private const string ReleaseSql =
    """
    UPDATE [HydrationQueue]
    SET [WorkerId] = NULL,
        [LockedUntil] = NULL
    WHERE [WorkerId] = @WorkerId
      AND [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
    """;

  private const string ResetStreamSql =
    """
    DELETE FROM [HydrationQueue]
    WHERE [StreamName] LIKE @StreamPrefix + '%'
      AND [ModelHash] = @ModelHash
    """;

  private static readonly string SafeInsertModelHashReadModelLockSql =
    $"""
     MERGE [ModelHashReadModelLocks] AS target
     USING (
       SELECT
         @ModelHash AS ModelHash,
         @ReadModelName AS ReadModelName,
         DATEADD(SECOND, {StreamLockLengthSeconds}, GETUTCDATE()) AS LockedUntil
     ) AS source
     ON target.[ModelHash] = source.ModelHash
     WHEN NOT MATCHED THEN
         INSERT (
           [ModelHash],
           [ReadModelName],
           [LockedUntil]
         )
         VALUES (
           source.ModelHash,
           source.ReadModelName,
           source.LockedUntil
         );
     """;

  private static readonly string TryModelHashReadModelLockSql =
    $"""
      UPDATE [ModelHashReadModelLocks]
      SET [LockedUntil] = DATEADD(SECOND, {StreamLockLengthSeconds}, GETUTCDATE())
      WHERE 
        [ReadModelName] = @ReadModelName
        AND 
         [LockedUntil] IS NULL 
         OR [LockedUntil] < GETUTCDATE()
         OR [ModelHash] = @ModelHash
     """;

  private static readonly string GetStreamLastHydratedPositionByHashSql =
    """
    SELECT TOP 1 [LastHydratedPosition]
    FROM [HydrationQueue]
    WHERE [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
    ORDER BY [LastHydratedPosition] DESC
    """;

  private static readonly string TryLockStreamSql =
    $"""
     UPDATE [HydrationQueue]
     SET [WorkerId] = @WorkerId,
         [LockedUntil] = DATEADD(SECOND, {StreamLockLengthSeconds}, GETUTCDATE()),
         [TimesLocked] = [TimesLocked] + 1
     WHERE ([LockedUntil] IS NULL OR [LockedUntil] < GETUTCDATE())
       AND [StreamName] = @StreamName
       AND [ModelHash] = @ModelHash
       AND [TimesLocked] = @TimesLocked
       AND ([WorkerId] IS NULL OR [WorkerId] = @ExistingWorkerId)
       AND [Position] = @Position
     """;

  private static readonly string TryRefreshStreamLockSql =
    $"""
     UPDATE [HydrationQueue]
     SET [LockedUntil] = DATEADD(SECOND, {StreamLockLengthSeconds}, GETUTCDATE())
     WHERE [WorkerId] = @WorkerId
       AND [StreamName] = @StreamName
       AND [ModelHash] = @ModelHash
     """;

  public static readonly string[] TableCreationScripts =
  [
    QueueTableCreationSql,
    GetCandidatesIndexCreationSql,
    TryLockIndexCreationSql,
    ModelHashReadModelLockTableCreationSql
  ];

  private readonly string connectionString;
  private readonly DatabaseHandlerFactory dbFactory;
  private readonly Fetcher fetcher;
  private readonly ILogger logger;
  private readonly string modelHash;

  private readonly IdempotentReadModel[] readModels;

  // ReSharper disable once NotAccessedField.Local
  private readonly Task task;

  private readonly SemaphoreSlim wakeUpSemaphore = new(1, 1);

  private readonly Guid workerId = Guid.NewGuid();
  private DateTime resumePollingAt = DateTime.MaxValue;

  private int waitBackoff = 1;

  public HydrationDaemonWorker(
    string modelHash,
    string connectionString,
    Fetcher fetcher,
    IdempotentReadModel[] readModels,
    DatabaseHandlerFactory dbFactory,
    MessageHub messageHub,
    ILogger logger)
  {
    this.modelHash = modelHash;
    this.connectionString = connectionString;
    this.fetcher = fetcher;
    this.readModels = readModels;
    this.dbFactory = dbFactory;
    this.logger = logger;
    task = Task.Run(Process);
    messageHub.Subscribe(this);
    this.logger.LogInformation("Hydration worker with ID {ID} started", workerId);
  }

  private DateTime ResumePollingAt
  {
    get => resumePollingAt;
    set
    {
      waitBackoff = value == DateTime.MinValue ? 1 : waitBackoff + 1;
      resumePollingAt = value;
    }
  }

  private bool ShouldPoll => ResumePollingAt <= DateTime.UtcNow;

  public static async Task Register(
    string modelHash,
    string connectionString,
    string streamName,
    StrongId id,
    ulong position,
    bool isDynamicConsistencyBoundary,
    IdempotentReadModel[] readModels)
  {
    if (!readModels.Any(rm => rm.CanProject(streamName)))
    {
      return;
    }

    var idType = id.GetType();
    await using var connection = new SqlConnection(connectionString);
    await connection.ExecuteAsync(
      UpsertSql,
      new
      {
        StreamName = streamName,
        SerializedId = Serialization.Serialize(id),
        IdTypeName = idType.Name,
        IdTypeNamespace = idType.Namespace,
        ModelHash = modelHash,
        Position = Convert.ToDecimal(position),
        IsDynamicConsistencyBoundary = isDynamicConsistencyBoundary
      });
  }

  public static async Task ResetStream(
    string modelHash,
    string connectionString,
    string streamPrefix)
  {
    await using var connection = new SqlConnection(connectionString);
    await connection.ExecuteAsync(
      ResetStreamSql,
      new
      {
        StreamPrefix = streamPrefix,
        ModelHash = modelHash
      });
  }

  public static async Task TryInitialLockReadModel(
    string modelHash,
    string readModelName,
    SqlConnection connection) =>
    await connection.ExecuteAsync(
      SafeInsertModelHashReadModelLockSql,
      new
      {
        ModelHash = modelHash,
        ReadModelName = readModelName
      });

  private async Task<bool> TryLockReadModel(string readModelName)
  {
    await using var connection = new SqlConnection(connectionString);
    return await connection.ExecuteAsync(
             TryModelHashReadModelLockSql,
             new
             {
               ModelHash = modelHash,
               ReadModelName = readModelName
             })
           > 0;
  }

  private async Task<ulong?> GetStreamLastHydratedPositionByHash(string otherHash, string streamName)
  {
    await using var connection = new SqlConnection(connectionString);
    var result = await connection.QuerySingleOrDefaultAsync<decimal?>(
      GetStreamLastHydratedPositionByHashSql,
      new { StreamName = streamName, ModelHash = otherHash });
    return result.HasValue ? Convert.ToUInt64(result.Value) : null;
  }

  private async Task<ForeignReadModelLock?> TryGetForeignHash(string readModelName)
  {
    await using var connection = new SqlConnection(connectionString);
    return await connection.QuerySingleOrDefaultAsync<ForeignReadModelLock>(
      "SELECT [ModelHash], [ReadModelName] FROM [ModelHashReadModelLocks] WHERE [ReadModelName] = @ReadModelName",
      new { ReadModelName = readModelName });
  }

  public void WakeUp()
  {
    wakeUpSemaphore.Wait();
    ResumePollingAt = DateTime.MinValue;
    wakeUpSemaphore.Release();
  }

  private async Task Process()
  {
    while (true)
    {
      try
      {
        if (!ShouldPoll)
        {
          await Task.Delay(Random.Shared.Next(1, 150));
          continue;
        }

        await TryProcess();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "error hydrating");
        await Task.Delay(Random.Shared.Next(1, 750));
      }
    }
    // ReSharper disable once FunctionNeverReturns
  }

  private async Task TryProcess()
  {
    HydrationQueueEntry? candidate;
    await using (var connection = new SqlConnection(connectionString))
    {
      var candidates =
        await connection.QueryAsync<HydrationQueueEntry>(GetCandidatesSql, new { ModelHash = modelHash });
      candidate = candidates.OrderBy(_ => Guid.NewGuid()).FirstOrDefault();

      if (candidate is null)
      {
        await wakeUpSemaphore.WaitAsync();
        ResumePollingAt = DateTime.UtcNow.AddMilliseconds(waitBackoff * 250 + Random.Shared.Next(1, 250));
        wakeUpSemaphore.Release();
        return;
      }

      var streamsLocked = await connection.ExecuteAsync(
        TryLockStreamSql,
        new
        {
          WorkerId = workerId,
          candidate.StreamName,
          ModelHash = modelHash,
          candidate.TimesLocked,
          ExistingWorkerId = candidate.WorkerId,
          candidate.Position
        });

      if (streamsLocked == 0)
      {
        await Task.Delay(15);
        return;
      }
    }

    var ableReadModels = readModels.Where(rm => rm.CanProject(candidate.StreamName)).ToArray();
    if (ableReadModels.Length == 0)
    {
      await MarkAsHydrated(candidate with { LastHydratedPosition = candidate.Position });
      return;
    }

    var cancellationSource = new CancellationTokenSource();
    var hydrateTask = Hydrate(cancellationSource.Token);
    var nextRefreshAt = DateTime.UtcNow.AddSeconds(2);

    while (!hydrateTask.IsCompleted)
    {
      if (nextRefreshAt < DateTime.UtcNow)
      {
        await using var connection = new SqlConnection(connectionString);
        var rowsUpdated = await connection.ExecuteAsync(
          TryRefreshStreamLockSql,
          new { WorkerId = workerId, candidate.StreamName, ModelHash = modelHash });
        if (rowsUpdated == 0)
        {
          await cancellationSource.CancelAsync();
          logger.LogWarning(
            "Lost lock on stream {Stream} at position {Position} for worker {WorkerId} and model {ModelHash}",
            candidate.StreamName,
            candidate.Position,
            workerId,
            modelHash);
          return;
        }

        nextRefreshAt = DateTime.UtcNow.AddSeconds(RefreshStreamLockFrequencySeconds);
      }

      // It might mask the error from the cancellation of the hydration.
      // ReSharper disable once MethodSupportsCancellation
      await Task.Delay(10);
    }

    await MarkAsHydrated(candidate with { LastHydratedPosition = await hydrateTask });
    return;

    async Task<ulong?> Hydrate(CancellationToken cancellationToken)
    {
      var maybeEntity = await candidate
        .GetStrongId()
        .Async()
        .Bind(id => fetcher
          .DaemonFetch(id, candidate.StreamName, candidate.IsDynamicConsistencyBoundary, cancellationToken)
          .Map(e => (id, e)));

      var lockedByOtherHash = new List<ForeignReadModelLock>();

      foreach (var t in maybeEntity)
      {
        foreach (var readModel in ableReadModels)
        {
          if (!await TryLockReadModel(readModel.TableName))
          {
            var owner = await TryGetForeignHash(readModel.TableName);
            if (owner != null && owner.ModelHash != modelHash)
            {
              lockedByOtherHash.Add(owner);
            }

            continue;
          }

          var processTask = readModel.TryProcess(t.e, dbFactory, t.id, null, logger, cancellationToken);
          _ = Task.Run(
            async () =>
            {
              while (processTask is not { IsCompleted: true })
              {
                await TryLockReadModel(readModel.TableName);
                await Task.Delay(TimeSpan.FromSeconds(RefreshStreamLockFrequencySeconds), cancellationToken);
              }
            },
            cancellationToken);
          await processTask;

          foreach (var other in lockedByOtherHash)
          {
            var otherPosition = await GetStreamLastHydratedPositionByHash(other.ModelHash, other.ReadModelName);
            if (otherPosition is null || otherPosition.Value < candidate.Position)
            {
              return null;
            }
          }
        }
      }

      return maybeEntity.Match(t => t.e.Position, () => null);
    }
  }

  private async Task MarkAsHydrated(HydrationQueueEntry entry)
  {
    await using var connection = new SqlConnection(connectionString);
    var rowsAffected = await connection.ExecuteAsync(
      UpdateHydrationState,
      new { entry.StreamName, ModelHash = modelHash, WorkerId = workerId, entry.Position, entry.LastHydratedPosition });
    if (rowsAffected == 0)
    {
      await connection.ExecuteAsync(
        ReleaseSql,
        new { entry.StreamName, ModelHash = modelHash, WorkerId = workerId });
    }
  }

  public static async Task<int> PendingEventsCount(string modelHash, string connectionString)
  {
    await using var connection = new SqlConnection(connectionString);
    return await connection.QuerySingleAsync<int>(PendingEventsCountSql, new { ModelHash = modelHash });
  }

  private record ForeignReadModelLock(string ModelHash, string ReadModelName);
}

public record HydrationQueueEntry(
  string StreamName,
  string SerializedId,
  string IdTypeName,
  string? IdTypeNamespace,
  string ModelHash,
  decimal Position,
  Guid? WorkerId,
  DateTime? LockedUntil,
  int TimesLocked,
  DateTime CreatedAt,
  bool IsDynamicConsistencyBoundary,
  decimal? LastHydratedPosition);
