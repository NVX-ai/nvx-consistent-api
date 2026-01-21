using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Nvx.ConsistentAPI;

public record TextSearchFilter(string ColumnName, StringValues ParameterValue, string ParameterName, string TableName)
  : ReadModelFilter
{
  public string WhereClause => $"[{TableName}].[{ColumnName}] LIKE '%' + @search{ColumnName} + '%'";
  public Dictionary<string, object> SqlParameters => new() { { ParameterName, ParameterValue } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 3 && kvp.Key.StartsWith("ts-"))
      .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
      .Select(kvp => new KeyValuePair<string, string>(kvp.Key[3..], kvp.Value.ToString()))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(string));

    foreach (var property in candidates)
    {
      yield return new TextSearchFilter(property.Name, columns[property.Name], $"search{property.Name}", tableName);
    }
  }

  public static IEnumerable<FilterDto> GenerateFilterDtos(Type readModelType)
  {
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(string));

    foreach (var property in candidates)
    {
      var fieldName = property.Name;
      var key = $"ts-{property.Name}";
      var description = $"Searches within {property.Name} for the given value.";

      yield return new FilterDto(
        fieldName,
        key,
        description,
        ReadModelFilter.FilterSchema(property.PropertyType));
    }
  }
}
