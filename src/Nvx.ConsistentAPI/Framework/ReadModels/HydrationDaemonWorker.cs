using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Framework;

namespace Nvx.ConsistentAPI;

public class HydrationDaemonWorker
{
  public const string QueueTableSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'HydrationQueue')
    BEGIN
    CREATE TABLE [dbo].[HydrationQueue](
        [StreamName] [nvarchar](3000) NOT NULL,
        [SerializedId] [nvarchar](max) NOT NULL,
        [IdTypeName] [nvarchar](256) NOT NULL,
        [IdTypeNamespace] [nvarchar](256) NULL,
        [ModelHash] [nvarchar](256) NOT NULL,
        [Position] [bigint] NOT NULL,
        [WorkerId] [uniqueidentifier] NULL,
        [LockedUntil] [datetime2](7) NULL,
        [TimesLocked] [int] NOT NULL,
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT (GETUTCDATE()),
        [IsDynamicConsistencyBoundary] [bit] NOT NULL DEFAULT (0))
    END
    """;

  // This prioritizes non-dynamic consistency boundary records to reduce the load, then will focus on the oldest
  // pending hydrations.
  private const string GetCandidatesSql =
    """
    SELECT TOP 50 *
    FROM [HydrationQueue]
    WHERE ([LockedUntil] IS NULL OR [LockedUntil] < GETUTCDATE())
      AND [ModelHash] = @ModelHash
      AND [TimesLocked] < 25
    ORDER BY [Position] ASC
    """;

  private const string TryLockStreamSql =
    """
    UPDATE [HydrationQueue]
    SET [WorkerId] = @WorkerId,
        [LockedUntil] = DATEADD(SECOND, 60, GETUTCDATE()),
        [TimesLocked] = [TimesLocked] + 1
    WHERE ([LockedUntil] IS NULL OR [LockedUntil] < GETUTCDATE())
      AND [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
      AND [TimesLocked] = @TimesLocked
      AND ([WorkerId] IS NULL AND @ExistingWorkerId IS NULL OR [WorkerId] = @ExistingWorkerId)
    """;

  private const string TryRefreshStreamLockSql =
    """
    UPDATE [HydrationQueue]
    SET [LockedUntil] = DATEADD(SECOND, 60, GETUTCDATE())
    WHERE [WorkerId] = @WorkerId
      AND [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
    """;

  private const string RemoveEntySql =
    """
    DELETE FROM [HydrationQueue]
    WHERE [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
      AND [WorkerId] = @WorkerId
      AND [Position] = @Position
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
          [Position] = CASE WHEN target.[Position] > source.Position THEN target.[Position] ELSE source.Position END,
          [IsDynamicConsistencyBoundary] = CASE WHEN target.[IsDynamicConsistencyBoundary] = 1 THEN 1 ELSE source.IsDynamicConsistencyBoundary END
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
    "SELECT COUNT(*) FROM [HydrationQueue] WHERE [ModelHash] = @ModelHash AND [TimesLocked] < 25";

  private const string ReleaseSql =
    """
    UPDATE [HydrationQueue]
    SET [WorkerId] = NULL,
        [LockedUntil] = NULL
    WHERE [WorkerId] = @WorkerId
      AND [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
    """;

  private readonly string connectionString;
  private readonly DatabaseHandlerFactory dbFactory;
  private readonly Fetcher fetcher;

  private readonly Lock @lock = new();
  private readonly ILogger logger;
  private readonly string modelHash;

  private readonly IdempotentReadModel[] readModels;

  private readonly Guid workerId = Guid.NewGuid();
  private bool shouldPoll;

  public HydrationDaemonWorker(
    string modelHash,
    string connectionString,
    Fetcher fetcher,
    IdempotentReadModel[] readModels,
    DatabaseHandlerFactory dbFactory,
    ILogger logger)
  {
    this.modelHash = modelHash;
    this.connectionString = connectionString;
    this.fetcher = fetcher;
    this.readModels = readModels;
    this.dbFactory = dbFactory;
    this.logger = logger;
    _ = Task.Run(Process);
  }

  public static async Task Register(
    string modelHash,
    string connectionString,
    string streamName,
    StrongId id,
    long position,
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
        Position = position,
        IsDynamicConsistencyBoundary = isDynamicConsistencyBoundary
      });
  }

  public void Trigger()
  {
    lock (@lock)
    {
      shouldPoll = true;
    }
  }

  private async Task Process()
  {
    while (true)
    {
      try
      {
        if (!shouldPoll)
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
        var count = await PendingEventsCount(modelHash, connectionString);
        lock (@lock)
        {
          shouldPoll = count > 0;
          return;
        }
      }

      var rowsAffected = await connection.ExecuteAsync(
        TryLockStreamSql,
        new
        {
          WorkerId = workerId,
          candidate.StreamName,
          ModelHash = modelHash,
          candidate.TimesLocked,
          ExistingWorkerId = candidate.WorkerId
        });

      if (rowsAffected == 0)
      {
        await Task.Delay(15);
        return;
      }
    }

    var ableReadModels = readModels.Where(rm => rm.CanProject(candidate.StreamName)).ToArray();
    if (ableReadModels.Length == 0)
    {
      await RemoveEntry(candidate);
      return;
    }

    var maybeEntity = await candidate
      .GetStrongId()
      .Async()
      .Bind(id => fetcher
        .DaemonFetch(id, candidate.StreamName, candidate.IsDynamicConsistencyBoundary)
        .Map(e => (id, e)));

    // This mechanism will benefit from a cancellation token.
    var hydrateTask = Hydrate();
    await Task.WhenAny(hydrateTask, RefreshLock());
    await RemoveEntry(candidate);
    return;

    async Task RefreshLock()
    {
      while (!hydrateTask.IsCompleted)
      {
        await Task.Delay(30_000);
        try
        {
          await using var connection = new SqlConnection(connectionString);
          await connection.ExecuteAsync(
            TryRefreshStreamLockSql,
            new { WorkerId = workerId, candidate.StreamName, ModelHash = modelHash });
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "error refreshing lock");
        }
      }
    }

    async Task Hydrate()
    {
      foreach (var t in maybeEntity)
      {
        foreach (var readModel in ableReadModels)
        {
          await readModel.TryProcess(t.e, dbFactory, t.id, null, logger);
        }
      }
    }
  }

  private async Task RemoveEntry(HydrationQueueEntry entry)
  {
    await using var connection = new SqlConnection(connectionString);
    var rowsAffected = await connection.ExecuteAsync(
      RemoveEntySql,
      new { entry.StreamName, ModelHash = modelHash, WorkerId = workerId, entry.Position });
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
}

public record HydrationQueueEntry(
  string StreamName,
  string SerializedId,
  string IdTypeName,
  string? IdTypeNamespace,
  string ModelHash,
  long Position,
  Guid? WorkerId,
  DateTime? LockedUntil,
  int TimesLocked,
  DateTime CreatedAt,
  bool IsDynamicConsistencyBoundary);
