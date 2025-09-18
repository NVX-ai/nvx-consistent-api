using Dapper;
using EventStore.Client;
using Microsoft.Data.SqlClient;
using Nvx.ConsistentAPI.Framework.Projections;

namespace Nvx.ConsistentAPI.InternalTooling;

public class ConsistencyCheck(
  string readModelsConnectionString,
  string modelHash,
  ReadModelHydrationDaemon daemon,
  EventModelingReadModelArtifact[] aggregatingReadModels,
  EventStoreClient eventStoreClient,
  DynamicConsistencyBoundaryDaemon dcbDaemon,
  ProjectionDaemon projectionDaemon)
{
  private readonly SemaphoreSlim semaphore = new(1, 1);
  private ulong highestAggregatingConsistency;
  private ulong highestCentralConsistency;
  private ulong highestConsistency;
  private ulong highestTodoConsistency;

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
      var dcbConsistent = DynamicConsistencyBoundaryConsistentAt(position);
      var projectionConsistent = ProjectionDaemonIsConsistentAt(position);
      var isConsistent =
        centralDaemonConsistent
        && aggregatingConsistent
        && todosProcessed
        && dcbConsistent
        && projectionConsistent;

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

  public bool AfterProcessingIsDone(ulong position) =>
    ProjectionDaemonIsConsistentAt(position) && DynamicConsistencyBoundaryConsistentAt(position);

  private bool ProjectionDaemonIsConsistentAt(ulong position) =>
    projectionDaemon.Insights(position).DaemonLastEventProjected is { } pos && pos >= position;

  private bool DynamicConsistencyBoundaryConsistentAt(ulong position) =>
    dcbDaemon.Insights(position).CurrentProcessedPosition >= position;

  private async Task<bool> CentralDaemonIsConsistentAt(ulong position)
  {
    if (highestCentralConsistency >= position)
    {
      return true;
    }

    if (await HydrationDaemonWorker.PendingEventsCount(modelHash, readModelsConnectionString, position) > 0)
    {
      return false;
    }

    var isConsistent = daemon.LastPosition is { } daemonPosition && daemonPosition.CommitPosition >= position;

    if (isConsistent && position > highestCentralConsistency)
    {
      highestCentralConsistency = position;
    }

    return isConsistent;
  }

  private async Task<bool> AggregatingConsistentAt(ulong position)
  {
    if (highestAggregatingConsistency >= position)
    {
      return true;
    }

    foreach (var readModel in aggregatingReadModels)
    {
      var insight = await readModel.Insights(position, eventStoreClient);
      if (!insight.IsCaughtUp)
      {
        return false;
      }
    }

    highestAggregatingConsistency =
      position > highestAggregatingConsistency
        ? position
        : highestAggregatingConsistency;

    return true;
  }

  private async Task<bool> TodosProcessedAt(ulong position)
  {
    if (highestTodoConsistency >= position)
    {
      return true;
    }

    var tableName = DatabaseHandler<TodoEventModelReadModel>.TableName(typeof(TodoEventModelReadModel));

    var query =
      $"""
       SELECT COUNT(1)
       FROM [{tableName}]
       WHERE [CompletedAt] IS NULL
         AND [IsFailed] = 0
         AND [EventPosition] IS NOT NULL
         AND [EventPosition] < @position
         AND [StartsAt] <= GETUTCDATE()
       """;

    await using var connection = new SqlConnection(readModelsConnectionString);
    var count = await connection.ExecuteScalarAsync<int>(query, new { position });

    if (count > 0)
    {
      return false;
    }

    highestTodoConsistency =
      position > highestTodoConsistency
        ? position
        : highestTodoConsistency;
    return true;
  }
}
