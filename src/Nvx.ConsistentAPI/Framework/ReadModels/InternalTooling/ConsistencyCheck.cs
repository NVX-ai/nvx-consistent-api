using Dapper;
using EventStore.Client;
using Microsoft.Data.SqlClient;

namespace Nvx.ConsistentAPI.InternalTooling;

public class ConsistencyCheck(
  string readModelsConnectionString,
  string modelHash,
  ReadModelHydrationDaemon daemon,
  EventModelingReadModelArtifact[] aggregatingReadModels,
  EventStoreClient eventStoreClient)
{
  private readonly SemaphoreSlim semaphore = new(1, 1);
  private ulong highestConsistency;

  public async Task<bool> IsConsistentAt(ulong position)
  {
    if (highestConsistency > position)
    {
      return true;
    }

    try
    {
      await semaphore.WaitAsync();
      if (highestConsistency > position)
      {
        return true;
      }

      var centralDaemonConsistent = await CentralDaemonIsConsistentAt(position);
      var aggregatingConsistent = await AggregatingConsistentAt(position);
      var todosProcessed = await TodosProcessedAt(position);
      var isConsistent = centralDaemonConsistent && aggregatingConsistent && todosProcessed;

      if (isConsistent && position > highestConsistency)
      {
        highestConsistency = position;
      }

      return isConsistent;
    }
    catch
    {
      return false;
    }
    finally
    {
      semaphore.Release();

    }
  }

  private async Task<bool> CentralDaemonIsConsistentAt(ulong position)
  {
    if (await HydrationDaemonWorker.PendingEventsCount(modelHash, readModelsConnectionString, position) > 0)
    {
      return false;
    }

    return daemon.LastPosition is { } daemonPosition && daemonPosition.CommitPosition >= position;
  }

  private async Task<bool> AggregatingConsistentAt(ulong position)
  {
    foreach (var readModel in aggregatingReadModels)
    {
      var insight = await readModel.Insights(position, eventStoreClient);
      if (!insight.IsCaughtUp)
      {
        return false;
      }
    }

    return true;
  }

  private async Task<bool> TodosProcessedAt(ulong position)
  {
    // Get the table name for todos
    var tableName = DatabaseHandler<TodoEventModelReadModel>.TableName(typeof(TodoEventModelReadModel));

    var query =
      $"""
       SELECT *
       FROM [{tableName}]
       WHERE [CompletedAt] IS NULL
         AND [IsFailed] = 0
         AND [EventPosition] IS NOT NULL
         AND [EventPosition] < @position
         AND [StartsAt] <= GETUTCDATE()
       """;

    await using var connection = new SqlConnection(readModelsConnectionString);
    var count = await connection.QueryAsync<dynamic>(query, new { position });

    return !count.Any();
  }
}
