using Microsoft.Data.SqlClient;

namespace Nvx.ConsistentAPI;

public class HydrationDaemonWorker(string modelHash, string connectionString)
{
  private readonly Guid workerId = Guid.NewGuid();

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
        [TimesLocked] [int] NOT NULL)
    END
    """;

  private async Task TryProcess()
  {
    await using var connection = new SqlConnection(connectionString);

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
  int TimesLocked);
