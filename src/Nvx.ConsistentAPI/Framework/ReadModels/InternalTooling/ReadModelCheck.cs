using Dapper;
using KurrentDB.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI.InternalTooling;

public class ReadModelCheck(
  ILogger logger,
  KurrentDBClient eventStoreClient,
  string readModelsConnectionString
)
{
  public async Task<bool> IsReadModelConsistentAt()
  {
    try
    {
      // Validate if latest position can be fetched
      var latestPosition = await eventStoreClient
        .ReadAllAsync(Direction.Backwards, Position.End, 1)
        .FirstOrDefaultAsync();
      if (!latestPosition.OriginalPosition.HasValue)
      {
        logger.LogWarning("Event store is empty. Assuming read model central daemon is consistent with the event store.");
        return true;
      }
      logger.LogInformation("Connected to event store at latest position {Position}", latestPosition.OriginalPosition.Value);

      // Check if table CentralDaemonHashedCheckpoints exists, if not we assume the read model central daemon has not processed any events yet and is consistent with the event store
      await using var connection = new SqlConnection(readModelsConnectionString);
      var centralDaemonCheckpointsTableExists = await connection.QueryFirstOrDefaultAsync<int>(
        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CentralDaemonHashedCheckpoints'") > 0;
      if (!centralDaemonCheckpointsTableExists)
      {
        logger.LogWarning("Table CentralDaemonHashedCheckpoints does not exist. Assuming read model central daemon has not processed any events yet and is consistent with the event store.");
        return true;
      }
      
      // Validate if latest Event Store position is later then read model central daemon position
      var readModelLatestPosition = await connection.QueryFirstOrDefaultAsync<ulong?>(
        "SELECT TOP 1 [Checkpoint] FROM [CentralDaemonHashedCheckpoints] ORDER BY [LastUpdatedAt] DESC");
      if (readModelLatestPosition != null && latestPosition.Event.Position.CommitPosition < readModelLatestPosition)
      {
        throw new InvalidOperationException(
          $"Event store is at position {latestPosition.Event.Position} but read models central daemon have processed up to {readModelLatestPosition}. Please ensure the event store is correctly configured and contains all events up to the latest position.");
      }
      
      // Validate if latest Event Store position is later then read model hydration queue position
      var readModelLatestHydrationPosition = await connection.QueryFirstOrDefaultAsync<ulong?>(
        "SELECT TOP 1 [Position] FROM [HydrationQueue] ORDER BY [Position] DESC");
      if (readModelLatestHydrationPosition != null && latestPosition.Event.Position.CommitPosition < readModelLatestHydrationPosition)
      {
        throw new InvalidOperationException(
          $"Event store is at position {latestPosition.Event.Position} but read model hydration queue has processed up to {readModelLatestHydrationPosition}. Please ensure the event store is correctly configured and contains all events up to the latest position.");
      }
      return true;
    }
    catch
    {
      return false;
    }
  }
}
