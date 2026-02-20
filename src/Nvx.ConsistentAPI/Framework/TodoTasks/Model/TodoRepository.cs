using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Data access layer that queries the SQL read model table for available and upcoming todo tasks.
/// </summary>
internal class TodoRepository(string connectionString, ILogger logger)
{
  private readonly string tableName =
    DatabaseHandler<TodoEventModelReadModel>.TableName(typeof(TodoEventModelReadModel));

  internal async Task<IEnumerable<TodoEventModelReadModel>> GetAboutToRunTodos()
  {
    try
    {
      const int batchSize = 45;
      var aMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
      var now = DateTime.UtcNow;

      var query =
        $"""
         SELECT TOP (@BatchSize)
             [Id],
             [RelatedEntityId],
             [StartsAt],
             [ExpiresAt],
             [CompletedAt],
             [JsonData],
             [Name],
             [LockedUntil],
             [SerializedRelatedEntityId],
             [EventPosition],
             [RetryCount],
             [IsFailed]
         FROM [{tableName}]
         WHERE
             ([StartsAt] <= @aMinuteAgo)
             AND [ExpiresAt] > @now
             AND ([LockedUntil] IS NULL OR [LockedUntil] < @aMinuteAgo)
             AND [IsFailed] = 0
             AND [CompletedAt] IS NULL
         ORDER BY [StartsAt] ASC
         """;

      await using var connection = new SqlConnection(connectionString);
      return await connection.QueryAsync<TodoEventModelReadModel>(
        query,
        new { BatchSize = batchSize, aMinuteAgo, now });
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed getting about to run todos");
      throw;
    }
  }

  internal async Task<Option<TodoEventModelReadModel>> GetNextAvailableTodo()
  {
    try
    {
      const int batchSize = 1;
      var now = DateTime.UtcNow;

      var query =
        $"""
          SELECT TOP (@BatchSize)
              [Id],
              [RelatedEntityId],
              [StartsAt],
              [ExpiresAt],
              [CompletedAt],
              [JsonData],
              [Name],
              [LockedUntil],
              [SerializedRelatedEntityId],
              [EventPosition],
              [RetryCount],
              [IsFailed]
          FROM [{tableName}]
          WHERE
              [StartsAt] <= @now
              AND [ExpiresAt] > @now
              AND ([LockedUntil] IS NULL OR [LockedUntil] < @now)
              AND [IsFailed] = 0
              AND [CompletedAt] IS NULL
          ORDER BY [StartsAt] ASC
         """;

      await using var connection = new SqlConnection(connectionString);
      return (await connection.QueryAsync<TodoEventModelReadModel>(
        query,
        new { BatchSize = batchSize, now })).SingleOrNone();
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed getting available todos");
      throw;
    }
  }
}
