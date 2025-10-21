using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text;
using Dapper;
using EventStore.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nvx.ConsistentAPI.Framework;

namespace Nvx.ConsistentAPI;

// ReSharper disable once NotAccessedPositionalProperty.Global
public record PageResult<T>(IEnumerable<T> Items, int Total, int Skipped);

public interface IsSoftDeleted;

public interface DatabaseHandler
{
  internal const string VersionSuffix = "0";
  string UpsertSql { get; }
  string AllColumns { get; }
  string AllColumnsTablePrefixed { get; }
  Task Initialize();
  string GenerateSafeInsertSql();
  string GenerateUpdateSql();
  string GetTableName();

  internal static string GetArrayPropTableName(string columnName, string tableName)
  {
    var toRemove = columnName.Length + VersionSuffix.Length + 3;
    var shorterTableName = $"{tableName[..^toRemove].Replace("ReadModel", string.Empty)}{VersionSuffix}";
    return $"{shorterTableName}{columnName}";
  }

  internal static int GetStringMaxLength(PropertyInfo propertyInfo) =>
    TryGetComponentModelMaxLength(propertyInfo) switch
    {
      { } a => a,
      _ => propertyInfo.Name switch
      {
        "Id" => StringSizes.InlinedId,
        _ => StringSizes.Default
      }
    };

  private static int? TryGetComponentModelMaxLength(PropertyInfo propertyInfo) =>
    propertyInfo.GetCustomAttribute<MaxLengthAttribute>() switch
    {
      { } a => a.Length,
      _ => propertyInfo.GetCustomAttribute<StringLengthAttribute>() switch
      {
        { } a => a.MaximumLength,
        _ => null
      }
    };
}

public class DatabaseHandler<Shape> : DatabaseHandler where Shape : HasId
{
  // ReSharper disable once StaticMemberInGenericType
  private static readonly string[] TraceabilityColumnNames =
  [
    nameof(TraceabilityFields.FrameworkCreatedAt),
    nameof(TraceabilityFields.FrameworkLastUpdatedAt),
    nameof(TraceabilityFields.FrameworkCreatedBy),
    nameof(TraceabilityFields.FrameworkLastUpdatedBy),
    nameof(TraceabilityFields.FrameworkRelatedEntityId),
    nameof(TraceabilityFields.FrameworkCreatedCommitPosition),
    nameof(TraceabilityFields.FrameworkLastUpdatedCommitPosition)
  ];

  // ReSharper disable once StaticMemberInGenericType
  private static readonly string TraceabilityColumns =
    $", {string.Join(", ", TraceabilityColumnNames.Select(c => $"[{c}]"))}";

  // ReSharper disable once StaticMemberInGenericType
  private static readonly string TraceabilityValues =
    $", {string.Join(", ", TraceabilityColumnNames.Select(c => $"@{c}"))}";

  // ReSharper disable once StaticMemberInGenericType
  private static readonly string TraceabilityUpdateColumns =
    $", {string.Join(", ", TraceabilityColumnNames.Select(c => $"[{c}] = Source.{c}"))}";

  private readonly PropertyInfo[] arrayProperties;

  // ReSharper disable once StaticMemberInGenericType
  private readonly string connectionString;
  private readonly SqlMapper.ITypeHandler handler = new JsonTypeHandler();
  private readonly bool isMultiTenant;
  private readonly bool isSoftDeleted;
  private readonly bool isUserBound;
  private readonly ILogger logger;
  private readonly Type shapeType;
  private readonly (PropertyInfo pi, int maxLength)[] stringProperties;
  private readonly string tableName;

  public DatabaseHandler(string connectionString, ILogger logger)
  {
    this.connectionString = connectionString;
    this.logger = logger;
    shapeType = typeof(Shape);
    tableName = TableName(shapeType);
    isUserBound = typeof(UserBound).IsAssignableFrom(shapeType);
    isSoftDeleted = typeof(IsSoftDeleted).IsAssignableFrom(shapeType);
    AllColumns = string.Join(
      ", ",
      shapeType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"[{p.Name}]"));
    AllColumnsTablePrefixed = string.Join(
      ", ",
      shapeType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"[{tableName}].[{p.Name}]"));
    UpsertSql = GenerateUpsertSql(false);
    TraceableUpsertSql = GenerateUpsertSql(true);
    arrayProperties = shapeType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.PropertyType == typeof(string[])
                  || p.PropertyType == typeof(int[])
                  || p.PropertyType == typeof(Guid[]))
      .ToArray();
    isMultiTenant = shapeType
      .GetInterfaces()
      .Any(i => i.Name == nameof(MultiTenantReadModel) && i.Namespace == typeof(MultiTenantReadModel).Namespace);
    stringProperties = shapeType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(string))
      .Select(p => (p, DatabaseHandler.GetStringMaxLength(p)))
      .ToArray();
  }

  public string TraceableUpsertSql { get; }

  public string AllColumnsTablePrefixed { get; }

  public string UpsertSql { get; }
  public string AllColumns { get; }

  public string GetTableName() => tableName;

  public string GenerateSafeInsertSql()
  {
    var columnValues = string.Join(
      ", ",
      shapeType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"@{p.Name}"));
    return $"""
              MERGE [{tableName}] AS Target
              USING
                (SELECT {columnValues})
              AS Source
                ({AllColumns})
              ON Target.Id = Source.Id
              WHEN NOT MATCHED BY TARGET THEN
                INSERT ({AllColumns})
                VALUES ({columnValues});
            """;
  }

  public string GenerateUpdateSql()
  {
    var updateFields = string.Join(
      ",",
      shapeType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"[{p.Name}] = @{p.Name}"));
    return $"UPDATE [{tableName}]\nSET {updateFields}\nWHERE [Id] = @Id;";
  }

  public async Task Initialize()
  {
    await using var connection = new SqlConnection(connectionString);

    const string createCheckpointsTableScript =
      """
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ReadModelCheckpoints')
        BEGIN
          CREATE TABLE [ReadModelCheckpoints]
          (
            [ModelName] NVARCHAR(255) PRIMARY KEY,
            [Checkpoint] NVARCHAR(255) NOT NULL
          )
        END
      """;

    const string createLockTableScript =
      """
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ReadModelLocks')
        BEGIN
          CREATE TABLE [ReadModelLocks]
          (
            [TableName] NVARCHAR(255) NOT NULL,
            [ProcessId] UNIQUEIDENTIFIER NOT NULL,
            [LockedUntil] DATETIME2 NOT NULL
          )
        END
      """;

    const string createTableLockUniqueTableNameScript =
      """
        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'UQ_ReadModelLocks_TableName')
        BEGIN
        CREATE UNIQUE INDEX [UQ_ReadModelLocks_TableName] ON [ReadModelLocks]([TableName])
        END
      """;

    const string createUpToDateReadModelScript =
      """
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UpToDateReadModels')
        BEGIN
          CREATE TABLE [UpToDateReadModels]
          (
            [ModelName] NVARCHAR(255) NOT NULL
          )
        END
      """;

    await connection.ExecuteAsync(createCheckpointsTableScript);
    await connection.ExecuteAsync(createLockTableScript);
    await connection.ExecuteAsync(createTableLockUniqueTableNameScript);
    await connection.ExecuteAsync(createUpToDateReadModelScript);
    await connection.ExecuteAsync(GenerateCreateTableScript());
    foreach (var listSql in GenerateCreateListTablesScript())
    {
      await connection.ExecuteAsync(listSql);
    }

    var daemonCheckpointTableCount = await connection.QueryFirstOrDefaultAsync<int>(
      "SELECT Count(*) FROM sys.tables WHERE name = 'CentralDaemonCheckpoint'");
    var daemonCheckpointHashedTableCount =
      await connection.QueryFirstOrDefaultAsync<int>(
        "SELECT Count(*) FROM sys.tables WHERE name = 'CentralDaemonHashedCheckpoints'");
    if (daemonCheckpointTableCount == 0 && daemonCheckpointHashedTableCount == 0)
    {
      await MarkAsUpToDate();
      return;
    }

    var checkpointCount = daemonCheckpointHashedTableCount == 0
      ? 0
      : await connection.QueryFirstOrDefaultAsync<int>("SELECT Count(*) FROM [CentralDaemonCheckpoint]");

    var hashedCheckpointCount = daemonCheckpointHashedTableCount == 0
      ? 0
      : await connection.QueryFirstOrDefaultAsync<int>("SELECT Count(*) FROM [CentralDaemonHashedCheckpoints]");

    if (checkpointCount == 0 && hashedCheckpointCount == 0)
    {
      await MarkAsUpToDate();
    }
  }

  public async Task<bool> IsUpToDate()
  {
    await using var connection = new SqlConnection(connectionString);
    const string sql = "SELECT 1 FROM [UpToDateReadModels] WHERE [ModelName] = @ModelName";
    return await connection.QueryFirstOrDefaultAsync<int>(sql, new { ModelName = tableName }) == 1;
  }

  public async Task MarkAsUpToDate()
  {
    await using var connection = new SqlConnection(connectionString);
    const string sql = "INSERT INTO [UpToDateReadModels] ([ModelName]) VALUES (@ModelName)";
    await connection.ExecuteAsync(sql, new { ModelName = tableName });
  }

  private IEnumerable<string> GenerateCreateListTablesScript()
  {
    foreach (var prop in arrayProperties)
    {
      var propTableName = DatabaseHandler.GetArrayPropTableName(prop.Name, tableName);
      var sqlType = MapToSqlType(prop, true);
      var sb = new StringBuilder();
      sb.AppendLine($"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{propTableName}')");
      sb.AppendLine("BEGIN");
      sb.AppendLine($"CREATE TABLE [{propTableName}] (");
      sb.AppendLine("    [Id] NVARCHAR(256) NOT NULL,");
      sb.AppendLine($"    [Value] {sqlType} NOT NULL,");
      sb.AppendLine(
        $"    CONSTRAINT [FK{propTableName}] FOREIGN KEY (Id) REFERENCES [{tableName}]([Id]) ON DELETE CASCADE");
      sb.AppendLine(");");
      sb.AppendLine($"CREATE NONCLUSTERED INDEX [IX_{propTableName}_Id] ON [{propTableName}] ([Id]);");
      sb.AppendLine("END");
      yield return sb.ToString();
    }
  }

  private string GenerateCreateTableScript()
  {
    var sb = new StringBuilder();
    sb.AppendLine($"IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{tableName}')");
    sb.AppendLine("BEGIN");
    sb.AppendLine($"CREATE TABLE {tableName} (");

    foreach (var prop in shapeType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
      var sqlType = MapToSqlType(prop);
      if (sqlType == "OBJECT")
      {
        sb.AppendLine($"    [{prop.Name}] NVARCHAR(MAX) {Nullability(prop)},");
        SqlMapper.AddTypeHandler(prop.PropertyType, handler);
        continue;
      }

      if (prop.Name == "Id")
      {
        sb.AppendLine($"    [{prop.Name}] {sqlType} UNIQUE {Nullability(prop)},");
      }
      else
      {
        sb.AppendLine($"    [{prop.Name}] {sqlType} {Nullability(prop)},");
      }
    }

    if (isSoftDeleted)
    {
      sb.AppendLine("    [IsDeleted] BIT NOT NULL DEFAULT 0,");
    }

    sb.AppendLine("    [FrameworkCreatedAt] DATETIME2 NULL,");
    sb.AppendLine("    [FrameworkLastUpdatedAt] DATETIME2 NULL,");
    sb.AppendLine("    [FrameworkCreatedBy] NVARCHAR(256) NULL,");
    sb.AppendLine("    [FrameworkLastUpdatedBy] NVARCHAR(256) NULL,");
    sb.AppendLine("    [FrameworkRelatedEntityId] NVARCHAR(256) NULL,");
    sb.AppendLine("    [FrameworkCreatedCommitPosition] NUMERIC(20, 0) NULL,");
    sb.AppendLine("    [FrameworkLastUpdatedCommitPosition] NUMERIC(20, 0) NULL");
    sb.AppendLine(");");
    sb.AppendLine("END");

    return sb.ToString();
  }

  private static string Nullability(PropertyInfo prop) => prop.IsNullable() ? "NULL" : "NOT NULL";

  internal static string MapToSqlType(PropertyInfo propertyInfo, bool isArray = false)
  {
    var type = isArray ? propertyInfo.PropertyType.GetElementType()! : propertyInfo.PropertyType;
    var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

    return underlyingType switch
    {
      _ when underlyingType == typeof(int) => "INT",
      _ when underlyingType == typeof(long) => "BIGINT",
      _ when underlyingType == typeof(float) => "REAL",
      _ when underlyingType == typeof(double) => "FLOAT",
      _ when underlyingType == typeof(bool) => "BIT",
      _ when underlyingType == typeof(string) => $"NVARCHAR({StringMaxLength()})",
      _ when underlyingType == typeof(char) => "NCHAR(1)",
      _ when underlyingType == typeof(DateTime) => "DATETIME2",
      _ when underlyingType == typeof(DateOnly) => "DATETIME2",
      _ when underlyingType == typeof(DateTimeOffset) => "DATETIMEOFFSET",
      _ when underlyingType == typeof(Guid) => "UNIQUEIDENTIFIER",
      _ when underlyingType == typeof(decimal) => "DECIMAL(18, 3)",
      _ when underlyingType == typeof(ulong) => "NUMERIC(20, 0)",
      _ => "OBJECT"
    };

    string StringMaxLength() =>
      DatabaseHandler.GetStringMaxLength(propertyInfo) switch
      {
        int.MaxValue => "MAX",
        var l => l.ToString()
      };
  }

  public static string TableName(Type type) =>
    $"{type.Name}{TypeHasher.ComputeTypeHash(type)}{DatabaseHandler.VersionSuffix}";

  internal async Task<Unit> UpdateArrayColumnsFor(string id)
  {
    await using var connection = new SqlConnection(connectionString);
    var sql = $"SELECT {AllColumnsTablePrefixed} FROM [{tableName}] WHERE [{tableName}].[Id] = @Id";
    var shape = await connection.QueryFirstOrDefaultAsync<Shape>(sql, new { Id = id });

    if (shape is null)
    {
      return unit;
    }

    foreach (var prop in arrayProperties)
    {
      var propTableName = DatabaseHandler.GetArrayPropTableName(prop.Name, tableName);
      await connection.ExecuteAsync(
        $"DELETE FROM [{propTableName}] WHERE [Id] = @Id",
        new { Id = id });

      foreach (var batch in BatchedArrayValues(prop, shape))
      {
        var values = batch.Select((_, i) => new { Id = id, Value = batch[i] });
        await connection.ExecuteAsync($"INSERT INTO [{propTableName}] VALUES (@Id, @Value)", values);
      }
    }

    return unit;
  }

  public async Task<Shape?> Find(
    string id,
    Du3<Guid, Guid[], Unit> tenancy,
    Option<UserSecurity> user,
    CustomFilter customFilter)
  {
    await using var connection = new SqlConnection(connectionString);
    var isApplicationAdmin = user.Map(u => u.ApplicationPermissions.Contains("admin")).DefaultValue(false);
    var tenantWhere = BuildTenantWhere(tenancy, customFilter.OverrideTenantFilter, isApplicationAdmin)
      .Map(w => $" AND {w}")
      .DefaultValue(string.Empty);
    var userWhere = isUserBound ? $" AND [{tableName}].[UserSub] = @UserSub" : string.Empty;
    var softDeleteWhere = isSoftDeleted ? $" AND [{tableName}].[IsDeleted] = 0" : string.Empty;
    var customWheres = customFilter.WhereClauses.Length != 0
      ? $" AND {string.Join(" AND ", customFilter.WhereClauses)}"
      : string.Empty;
    var join = customFilter.JoinClause is not null ? $"\n {customFilter.JoinClause} \n" : string.Empty;
    var sql =
      $"""
       SELECT {AllColumnsTablePrefixed}
       FROM [{tableName}]
       {join} 
       WHERE [{tableName}].[Id] = @Id{tenantWhere}{userWhere}{softDeleteWhere}{customWheres}
       """;

    var parameters = new DynamicParameters();
    parameters.Add("Id", id);
    parameters.Add("UserSub", user.Map(u => u.Sub).DefaultValue(Guid.NewGuid().ToString()));

    foreach (var tenantTuple in BuildTenancyParams(tenancy, isApplicationAdmin))
    {
      parameters.Add(tenantTuple.name, tenantTuple.id);
    }

    return await connection.QueryFirstOrDefaultAsync<Shape>(sql, parameters);
  }

  private Option<string> BuildTenantWhere(Du3<Guid, Guid[], Unit> tenancy, bool isOverriden, bool isAdmin)
  {
    if (isOverriden)
    {
      return None;
    }

    var tenantsTableName = DatabaseHandler.GetArrayPropTableName(nameof(MultiTenantReadModel.TenantIds), tableName);

    return tenancy
      .Match<Option<string>>(
        _ => $"[{tableName}].[TenantId] = @tenantId",
        ids => isMultiTenant && !isAdmin
          ? ids.Length > 0
            ? $"(SELECT COUNT(1) FROM [{tenantsTableName}] [tt] WHERE [tt].[Id] = [Id] AND [tt].[Value] IN ({ToParams(ids)})) > 0"
            : "1 = 2"
          : None,
        _ => None);

    string ToParams(Guid[] ids) => string.Join(", ", ids.Select((_, idx) => $"@MultiTenancyFilter{idx}"));
  }

  private static IEnumerable<(string name, Guid id)> BuildTenancyParams(
    Du3<Guid, Guid[], Unit> tenancy,
    bool isApplicationAdmin) =>
    tenancy.Match<IEnumerable<(string name, Guid id)>>(
      tenantId => [("tenantId", tenantId)],
      ids => isApplicationAdmin ? [] : ids.Select((tenantId, index) => ($"MultiTenancyFilter{index}", tenantId)),
      _ => []
    );

  public async Task<PageResult<Shape>> GetPage(
    int pageNumber,
    int pageSize,
    IQueryCollection queryParams,
    Option<UserSecurity> user,
    SortBy? sortBy,
    Du3<Guid, Guid[], Unit> tenancy,
    CustomFilter customFilter)
  {
    var offset = Math.Max(0, pageNumber) * pageSize;

    var filters = ReadModelFilter.Get(typeof(Shape), queryParams, tableName).ToArray();

    var isApplicationAdmin = user.Map(u => u.ApplicationPermissions.Contains("admin")).DefaultValue(false);
    var clauses = filters
      .Select(f => f.WhereClause)
      .Select(Some)
      .Append(BuildTenantWhere(tenancy, customFilter.OverrideTenantFilter, isApplicationAdmin))
      .Append(isUserBound ? $"[{tableName}].[UserSub] = @userSub" : None)
      .Append(isSoftDeleted ? $"[{tableName}].[IsDeleted] = @isDeleted" : None)
      .Choose()
      .Concat(customFilter.WhereClauses)
      .ToArray();

    var filterSql = clauses.Length != 0
      ? $"WHERE {string.Join(" AND ", clauses)}"
      : string.Empty;

    var hasAdditionalColumns = !string.IsNullOrWhiteSpace(customFilter.AdditionalColumns);

    var columnsSelect = hasAdditionalColumns
      ? $"SELECT {AllColumns} FROM (SELECT {AllColumnsTablePrefixed}, {customFilter.AdditionalColumns}"
      : $"SELECT {AllColumnsTablePrefixed}";

    var countSelect = hasAdditionalColumns
      ? $"SELECT Count(1) FROM (SELECT {AllColumnsTablePrefixed}, {customFilter.AdditionalColumns}"
      : "SELECT Count(1)";

    var queryClose = hasAdditionalColumns ? ") as [MainTable]" : string.Empty;

    var selectSql = $"{columnsSelect} FROM {tableName} \n {customFilter.JoinClause} \n";
    var orderSql = $"ORDER BY {SortColumn(shapeType, sortBy)} {Direction(sortBy)}";
    const string offsetSql = "OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

    var rowsSql = $"{selectSql} {filterSql}{queryClose} {orderSql} {offsetSql};";
    var countSql = $"{countSelect} FROM {tableName} \n {customFilter.JoinClause} \n {filterSql}{queryClose};";
    var parameters = new DynamicParameters();
    parameters.Add("offset", offset);
    parameters.Add("pageSize", pageSize);
    foreach (var tenantTuple in BuildTenancyParams(tenancy, isApplicationAdmin))
    {
      parameters.Add(tenantTuple.name, tenantTuple.id);
    }

    parameters.Add("userSub", user.Map(u => u.Sub).DefaultValue(Guid.NewGuid().ToString));
    parameters.Add("isDeleted", false);

    foreach (var filter in filters)
    foreach (var parameter in filter.SqlParameters)
    {
      parameters.Add(parameter.Key, parameter.Value);
    }

    var rows = GetRows();
    var count = GetCount();

    return new PageResult<Shape>(await rows, await count, offset);

    async Task<IEnumerable<Shape>> GetRows()
    {
      await using var connection = new SqlConnection(connectionString);
      return await connection.QueryAsync<Shape>(rowsSql, parameters);
    }

    async Task<int> GetCount()
    {
      await using var connection = new SqlConnection(connectionString);
      return await connection.ExecuteScalarAsync<int>(countSql, parameters);
    }
  }

  private async Task Delete(StrongId id, SqlConnection connection, CancellationToken cancellationToken)
  {
    try
    {
      await connection.ExecuteAsync(
        new CommandDefinition(
          $"DELETE FROM [{tableName}] WHERE [FrameworkRelatedEntityId] = @Id",
          new { Id = id.StreamId() },
          cancellationToken: cancellationToken));
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock
    {
      await Task.Delay(Random.Shared.Next(150), cancellationToken);
      await Delete(id, connection, cancellationToken);
    }
  }

  public async Task<Unit> Update(
    Shape[] rms,
    string? checkpoint,
    TraceabilityFields traceabilityFields,
    StrongId id,
    CancellationToken cancellationToken)
  {
    await using var connection = new SqlConnection(connectionString);

    await Delete(id, connection, cancellationToken);
    try
    {
      foreach (var rm in rms.DistinctBy(r => r.Id))
      {
        await Upsert(rm, traceabilityFields, connection, cancellationToken);
      }

      if (checkpoint is not null)
      {
        await UpdateCheckpoint(connection, checkpoint, null, cancellationToken);
      }
    }
    catch (Exception ex) when (!ex.Message.Contains("Cannot insert duplicate key in object"))
    {
      logger.LogError(ex, "Failed to upsert {ShapeType} {Id}", shapeType, id);
      throw;
    }

    return unit;
  }

  private async Task Upsert(
    Shape rm,
    TraceabilityFields traceabilityFields,
    SqlConnection connection,
    CancellationToken cancellationToken)
  {
    try
    {
      var parameters = new DynamicParameters(rm);

      var traceabilityFieldsTruncated = traceabilityFields with
      {
        FrameworkRelatedEntityId = traceabilityFields.FrameworkRelatedEntityId.Length > StringSizes.InlinedId
          ? new StringInfo(traceabilityFields.FrameworkRelatedEntityId).SubstringByTextElements(
            0,
            StringSizes.InlinedId)
          : traceabilityFields.FrameworkRelatedEntityId
      };
      parameters.AddDynamicParams(traceabilityFieldsTruncated);

      foreach (var prop in stringProperties)
      {
        var strValue = prop.pi.GetValue(rm) as string;
        if (string.IsNullOrEmpty(strValue))
        {
          continue;
        }

        var clampedValue = strValue.Length > prop.maxLength
          ? new StringInfo(strValue).SubstringByTextElements(0, prop.maxLength)
          : strValue;
        parameters.Add(prop.pi.Name, clampedValue);
      }

      foreach (var prop in arrayProperties)
      {
        foreach (var batch in BatchedArrayValues(prop, rm))
        {
          var values = batch
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            .Where(y => y != null)
            .ToArray();

          if (values.Length == 0)
          {
            continue;
          }

          parameters.Add(prop.Name, JsonConvert.SerializeObject(values));
        }
      }

      await connection.ExecuteAsync(
        new CommandDefinition(TraceableUpsertSql, parameters, cancellationToken: cancellationToken));
      foreach (var prop in arrayProperties)
      {
        var propTableName = DatabaseHandler.GetArrayPropTableName(prop.Name, tableName);

        foreach (var batch in BatchedArrayValues(prop, rm))
        {
          // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
          var values = batch.Where(y => y != null).ToArray();

          if (values.Length == 0)
          {
            continue;
          }

          var arrayParamNames = string.Join(", ", values.Select((_, i) => $"(@Id, @Value{i})"));

          var arrayParams = new DynamicParameters();
          arrayParams.Add("Id", rm.Id);
          foreach (var tuple in values.Select((v, i) => (v, i)))
          {
            arrayParams.Add($"Value{tuple.i}", tuple.v);
          }

          await connection.ExecuteAsync(
            new CommandDefinition(
              $"INSERT INTO [{propTableName}] ([Id], [Value]) VALUES {arrayParamNames}",
              arrayParams,
              cancellationToken: cancellationToken));
        }
      }
    }
    catch (Exception ex) when (!ex.Message.Contains("Cannot insert duplicate key in object"))
    {
      logger.LogError(ex, "Failed to upsert {ShapeType} {Id}", shapeType, rm.Id);
      throw;
    }
  }

  private static IEnumerable<object[]> BatchedArrayValues(PropertyInfo prop, Shape entity)
  {
    const int batchSize = 750;
    var value = prop.GetValue(entity);
    var values = value?.GetType() switch
    {
      { } t when t == typeof(int[]) => ((int[])value).Select(object (v) => v).ToArray(),
      { } t when t == typeof(Guid[]) => ((Guid[])value).Select(object (v) => v).ToArray(),
      not null => (object[]?)value,
      null => null
    };

    if (values is null)
    {
      yield break;
    }

    for (var i = 0; i < values.Length; i += batchSize)
    {
      yield return values.Skip(i).Take(batchSize).ToArray();
    }
  }

  public async Task<Position> Checkpoint()
  {
    await using var connection = new SqlConnection(connectionString);
    const string sql = "SELECT [Checkpoint] FROM [ReadModelCheckpoints] WHERE [ModelName] = @ModelName";
    var value = await connection.QueryFirstOrDefaultAsync<string?>(sql, new { ModelName = tableName });
    if (value is null)
    {
      return Position.Start;
    }

    return Position.TryParse(value, out var position) && position != null
      ? position.Value
      : Position.Start;
  }

  private string GenerateUpsertSql(bool isTraceable)
  {
    var properties = shapeType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
    var columnValues =
      $"{string.Join(", ", properties.Select(p => $"@{p.Name}"))}{(isTraceable ? TraceabilityValues : string.Empty)}";
    var updateColumns =
      $"{string.Join(", ", properties.Select(p => $"[{p.Name}] = Source.{p.Name}"))}{(isTraceable ? TraceabilityUpdateColumns : string.Empty)}";
    var columns =
      $"{AllColumns}{(isTraceable ? TraceabilityColumns : string.Empty)}";

    return $"""
              MERGE [{tableName}] AS Target
              USING
                (SELECT {columnValues})
              AS Source
                ({columns})
              ON Target.Id = Source.Id
              WHEN MATCHED THEN
                UPDATE SET {updateColumns}
              WHEN NOT MATCHED BY TARGET THEN
                INSERT ({columns})
                VALUES ({columnValues});
            """;
  }

  public async Task<bool> TryAcquireLock(Guid processId, CancellationToken token)
  {
    const string tryLockSql =
      """
      MERGE [ReadModelLocks] WITH (ROWLOCK) AS Target
      USING (
        SELECT
          @TableName AS TableName,
          @ProcessId AS ProcessId,
          DATEADD(SECOND, 25, GETUTCDATE()) AS LockedUntil
      ) AS Source
      ON Target.[TableName] = Source.[TableName]
      WHEN MATCHED AND (Target.[LockedUntil] < GETUTCDATE() OR Target.[ProcessId] = Source.[ProcessId]) THEN
        UPDATE SET
          [ProcessId] = Source.ProcessId,
          [LockedUntil] = Source.LockedUntil
      WHEN NOT MATCHED THEN
        INSERT ([TableName], [ProcessId], [LockedUntil])
        VALUES (Source.TableName, Source.ProcessId, Source.LockedUntil);
      """;

    try
    {
      await using var connection = new SqlConnection(connectionString);
      return await connection
               .ExecuteAsync(
                 new CommandDefinition(
                   tryLockSql,
                   new { TableName = tableName, ProcessId = processId },
                   cancellationToken: token))
             > 0;
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to acquire lock for {TableName}", tableName);
      return false;
    }
  }

  public async Task<bool> TryRefreshLock(Guid processId, CancellationToken token)
  {
    const string refreshLockSql =
      """
        UPDATE [ReadModelLocks] WITH (ROWLOCK)
        SET [LockedUntil] = DATEADD(SECOND, 25, GETUTCDATE())
        WHERE
          [TableName] = @TableName
          AND [ProcessId] = @ProcessId
          AND GETUTCDATE() < [LockedUntil]
      """;
    try
    {
      await using var connection = new SqlConnection(connectionString);
      return await connection.ExecuteAsync(refreshLockSql, new { TableName = tableName, ProcessId = processId }) > 0;
    }
    catch
    {
      return false;
    }
  }

  public async Task Reset(bool dropTable = true)
  {
    await using var connection = new SqlConnection(connectionString);
    await connection.ExecuteAsync(
      "DELETE FROM [ReadModelCheckpoints] WHERE [ModelName] = @ModelName",
      new { ModelName = tableName });
    await connection.ExecuteAsync(
      "DELETE FROM [UpToDateReadModels] WHERE [ModelName] = @ModelName",
      new { ModelName = tableName });

    var action = dropTable ? "DROP TABLE" : "DELETE FROM";

    foreach (var ap in arrayProperties)
    {
      var apTableName = DatabaseHandler.GetArrayPropTableName(ap.Name, tableName);
      await connection.ExecuteAsync($"{action} [{apTableName}]");
    }

    await connection.ExecuteAsync($"{action} [{tableName}]");

    await Initialize();
  }

  public async Task UpdateCheckpoint(string checkpoint)
  {
    await using var connection = new SqlConnection(connectionString);
    await connection.ExecuteAsync(
      """
        IF EXISTS (SELECT 1 FROM [ReadModelCheckpoints] WHERE [ModelName] = @ModelName)
        UPDATE [ReadModelCheckpoints] SET [Checkpoint] = @Checkpoint WHERE [ModelName] = @ModelName
        ELSE
        INSERT INTO [ReadModelCheckpoints] ([ModelName], [Checkpoint]) VALUES (@ModelName, @Checkpoint)
      """,
      new { ModelName = tableName, Checkpoint = checkpoint }
    );
  }

  internal async Task UpdateCheckpoint(
    IDbConnection connection,
    string checkpoint,
    IDbTransaction? transaction,
    CancellationToken cancellationToken = default)
  {
    const string sqlCheckpoint =
      """
        IF EXISTS (SELECT 1 FROM [ReadModelCheckpoints] WHERE [ModelName] = @ModelName)
        UPDATE [ReadModelCheckpoints] SET [Checkpoint] = @Checkpoint WHERE [ModelName] = @ModelName
        ELSE
        INSERT INTO [ReadModelCheckpoints] ([ModelName], [Checkpoint]) VALUES (@ModelName, @Checkpoint)
      """;

    await connection.ExecuteAsync(
      new CommandDefinition(
        sqlCheckpoint,
        new { ModelName = tableName, Checkpoint = checkpoint },
        transaction,
        cancellationToken: cancellationToken));
  }

  private static string SortColumn(Type type, SortBy? sortBy)
  {
    var sortProperty =
      sortBy is null
        ? null
        : type.GetProperty(
          sortBy.Field,
          BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

    return sortProperty?.Name ?? "Id";
  }

  private static string Direction(SortBy? sortBy) =>
    (sortBy?.Direction ?? SortDirection.Ascending) == SortDirection.Ascending ? "ASC" : "DESC";
}

public record ReadModelLock(string TableName, Guid ProcessId, DateTime LockedUntil);

public class JsonTypeHandler : SqlMapper.ITypeHandler
{
  public void SetValue(IDbDataParameter parameter, object value) =>
    parameter.Value = JsonConvert.SerializeObject(value);

  public object Parse(Type destinationType, object value) =>
    JsonConvert.DeserializeObject((string)value, destinationType)!;
}

public class DateTimeTypeHandler : SqlMapper.ITypeHandler
{
  private static readonly DateTime SqlMinValue = new(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc);

  public void SetValue(IDbDataParameter parameter, object value)
  {
    if (value is DateTime dateTime)
    {
      var asUtc = dateTime.ToUniversalTime();
      parameter.Value = asUtc < SqlMinValue ? SqlMinValue : asUtc;
      return;
    }

    parameter.Value = value;
  }

  public object? Parse(Type destinationType, object value)
  {
    if (destinationType == typeof(DateTime?) && value is DBNull or null)
    {
      return null;
    }

    if (value is DateTime dateTime)
    {
      return dateTime.Kind switch
      {
        DateTimeKind.Local => dateTime.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
        _ => dateTime
      };
    }

    return value;
  }
}

public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
  public override void SetValue(IDbDataParameter parameter, DateOnly date) =>
    parameter.Value = date.ToDateTime(new TimeOnly(0, 0));

  public override DateOnly Parse(object value) => DateOnly.FromDateTime((DateTime)value);
}

public class DateOnlyNullableTypeHandler : SqlMapper.TypeHandler<DateOnly?>
{
  public override void SetValue(IDbDataParameter parameter, DateOnly? date) =>
    parameter.Value = date?.ToDateTime(new TimeOnly(0, 0));

  public override DateOnly? Parse(object? value) => value is null ? null : DateOnly.FromDateTime((DateTime)value);
}

public class ULongTypeHandler : SqlMapper.TypeHandler<ulong>
{
  public override void SetValue(IDbDataParameter parameter, ulong value) =>
    parameter.Value = (decimal)value;

  public override ulong Parse(object value) => value switch
  {
    decimal d => (ulong)d,
    long l => (ulong)l,
    int i => (ulong)i,
    _ => Convert.ToUInt64(value)
  };
}

public class ULongNullableTypeHandler : SqlMapper.TypeHandler<ulong?>
{
  public override void SetValue(IDbDataParameter parameter, ulong? value) =>
    parameter.Value = value.HasValue ? (decimal)value.Value : DBNull.Value;

  public override ulong? Parse(object value) => value switch
  {
    decimal d => (ulong)d,
    long l => (ulong)l,
    int i => (ulong)i,
    DBNull => null,
    null => null,
    _ => Convert.ToUInt64(value)
  };
}

public record TraceabilityFields(
  DateTime FrameworkCreatedAt,
  DateTime FrameworkLastUpdatedAt,
  string? FrameworkCreatedBy,
  string? FrameworkLastUpdatedBy,
  string FrameworkRelatedEntityId,
  ulong? FrameworkCreatedCommitPosition,
  ulong? FrameworkLastUpdatedCommitPosition
  );
