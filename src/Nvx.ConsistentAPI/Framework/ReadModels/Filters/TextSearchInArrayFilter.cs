using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Nvx.ConsistentAPI;

public record TextSearchInArrayFilter(
  string ColumnName,
  StringValues ParameterValue,
  string ParameterName,
  string TableName)
  : ReadModelFilter
{
  private readonly string arrayTableName = DatabaseHandler.GetArrayPropTableName(ColumnName, TableName);

  public string WhereClause =>
    $"""
     (
      SELECT COUNT(*)
      FROM [{arrayTableName}]
      WHERE LOWER([{arrayTableName}].[Value]) LIKE '%' + LOWER(@tsArray{ColumnName}) + '%'
        AND [{arrayTableName}].[Id] = [{TableName}].[Id]
     ) > 0
     """;

  public Dictionary<string, object> SqlParameters => new() { { ParameterName, ParameterValue } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 4 && kvp.Key.StartsWith("tsa-"))
      .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
      .Select(kvp => new KeyValuePair<string, string>(kvp.Key[4..], kvp.Value.ToString()))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => p.PropertyType.GetElementType()! == typeof(string)
                  || p.PropertyType.GetElementType()! == typeof(int)
                  || p.PropertyType.GetElementType()! == typeof(Guid));

    foreach (var property in candidates)
    {
      yield return new TextSearchInArrayFilter(
        property.Name,
        columns[property.Name],
        $"tsArray{property.Name}",
        tableName);
    }
  }

  public static IEnumerable<FilterDto> GenerateFilterDtos(Type readModelType)
  {
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.PropertyType.GetElementType()! == typeof(string)
                  || p.PropertyType.GetElementType()! == typeof(int)
                  || p.PropertyType.GetElementType()! == typeof(Guid));

    foreach (var property in candidates)
    {
      var fieldName = property.Name;
      var key = $"tsa-{fieldName}";
      var description = $"Filters records where {fieldName} is found the given array.";

      yield return new FilterDto(
        fieldName,
        key,
        description,
        ReadModelFilter.FilterSchema(property.PropertyType, true));
    }
  }
}
