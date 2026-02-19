using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI;

public class DatabaseHandlerFactory(string connectionString, ILogger logger) : ReadModelDetailsFactory
{
  public TableDetails GetTableDetails<ReadModel>() where ReadModel : EventModelReadModel
  {
    var handler = Get<ReadModel>();
    return new TableDetails(
      handler.GetTableName(),
      handler.UpsertSql,
      handler.TraceableUpsertSql,
      handler.GenerateSafeInsertSql(),
      handler.GenerateUpdateSql(),
      handler.AllColumns,
      new Dictionary<Type, AdditionalTableDetails>(),
      handler.AllColumnsTablePrefixed);
  }

  internal DatabaseHandler<ReadModel> Get<ReadModel>() where ReadModel : EventModelReadModel =>
    new(connectionString, logger);

  // Meant to be accessed by the TodoProcessor
  // ReSharper disable once UnusedMember.Global
  public async Task<IEnumerable<T>> Query<T>(Func<SqlConnection, Task<IEnumerable<T>>> query)
  {
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    return await query(connection);
  }

  // Meant to be accessed by the TodoProcessor
  // ReSharper disable once UnusedMember.Global
  public async Task<IEnumerable<ReadModel>> Query<ReadModel>(
    Func<TableDetails, string> query,
    object parameters) where ReadModel : EventModelReadModel
  {
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    return await connection.QueryAsync<ReadModel>(query(GetTableDetails<ReadModel>()), parameters);
  }
}
