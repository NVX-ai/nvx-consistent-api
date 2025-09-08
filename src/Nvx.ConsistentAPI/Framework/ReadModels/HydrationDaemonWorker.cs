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
        [TimesLocked] [int] NOT NULL)
    END
    """;
}
