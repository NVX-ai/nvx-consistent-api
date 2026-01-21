using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Nvx.ConsistentAPI;

public record IsArrayNotEmptyFilter(
  // ReSharper disable once NotAccessedPositionalProperty.Global
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
      WHERE [{arrayTableName}].[Id] = [{TableName}].[Id]
     ) > 0
     """;

  public Dictionary<string, object> SqlParameters => new() { { ParameterName, ParameterValue } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 4 && kvp.Key.StartsWith("ane-"))
      .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
      .Select(kvp => new KeyValuePair<string, string>(kvp.Key[4..], kvp.Value.ToString()))
      .Where(kvp => bool.TryParse(kvp.Value, out var value) && value)
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => p.PropertyType.GetElementType()! == typeof(string)
                  || p.PropertyType.GetElementType()! == typeof(int));

    foreach (var property in candidates)
    {
      yield return new IsArrayNotEmptyFilter(
        property.Name,
        columns[property.Name],
        $"isArrayNotEmpty{property.Name}",
        tableName);
    }
  }

  public static IEnumerable<FilterDto> GenerateFilterDtos(Type readModelType)
  {
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.PropertyType.GetElementType()! == typeof(string)
                  || p.PropertyType.GetElementType()! == typeof(int));

    foreach (var property in candidates)
    {
      var fieldName = property.Name;
      var key = $"ane-{property.Name}";
      var description = $"Filters records where {fieldName} is not an empty array.";

      yield return new FilterDto(fieldName, key, description, ReadModelFilter.FilterSchema(typeof(bool)));
    }
  }
}
