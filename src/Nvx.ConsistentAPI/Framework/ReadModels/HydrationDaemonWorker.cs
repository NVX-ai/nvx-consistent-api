using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

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
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT (GETUTCDATE()))
    END
    """;

  private const string GetCandidatesSql =
    """
    SELECT TOP 50 *
    FROM [HydrationQueue]
    WHERE ([LockedUntil] IS NULL OR [LockedUntil] < GETUTCDATE())
      AND [ModelHash] = @ModelHash
      AND [TimesLocked] < 25
    ORDER BY [Position] ASC
    """;

  private const string TryReserveStream =
    """
    UPDATE [HydrationQueue]
    SET [WorkerId] = @WorkerId,
        [LockedUntil] = DATEADD(SECOND, 240, GETUTCDATE()),
        [TimesLocked] = @TimesLocked + 1
    WHERE ([LockedUntil] IS NULL OR [LockedUntil] < GETUTCDATE())
      AND [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
      AND [TimesLocked] = @TimesLocked
    """;

  public const string RemoveEntySql =
    """
    DELETE FROM [HydrationQueue]
    WHERE [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
    """;

  private readonly string connectionString;
  private readonly DatabaseHandlerFactory dbFactory;
  private readonly Fetcher fetcher;

  private readonly Lock @lock = new();
  private readonly ILogger logger;
  private readonly string modelHash;

  // ReSharper disable once NotAccessedField.Local
  private readonly Task processTask;
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
    processTask = Process();
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
      if (!shouldPoll)
      {
        await Task.Delay(Random.Shared.Next(1, 500));
        continue;
      }

      try
      {
        await TryProcess();
      }
      catch
      {
        // ignore for now
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
      lock (@lock)
      {
        if (candidate is null)
        {
          shouldPoll = false;
          return;
        }
      }

      var rowsAffected = await connection.ExecuteAsync(
        TryReserveStream,
        new
        {
          WorkerId = workerId,
          candidate.StreamName,
          ModelHash = modelHash,
          candidate.TimesLocked
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
      return;
    }

    var maybeEntity = await candidate
      .GetStrongId()
      .Async()
      .Bind(id => fetcher.DaemonFetch(id, candidate.StreamName).Map(e => (id, e)));

    foreach (var t in maybeEntity)
    {
      foreach (var readModel in ableReadModels)
      {
        await readModel.TryProcess(t.e, dbFactory, t.id, null, logger);
      }
    }

    await using (var connection = new SqlConnection(connectionString))
    {
      await connection.ExecuteAsync(RemoveEntySql, new { candidate.StreamName, ModelHash = modelHash });
    }
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
  DateTime CreatedAt);
