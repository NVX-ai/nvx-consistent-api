using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Nvx.ConsistentAPI;

public record IsNullFilter(string ColumnName, StringValues ParameterValue, string ParameterName, string TableName)
  : ReadModelFilter
{
  public string WhereClause => $"[{TableName}].[{ColumnName}] IS NULL";
  public Dictionary<string, object> SqlParameters => new() { { ParameterName, ParameterValue } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 3 && kvp.Key.StartsWith("in-"))
      .Where(kvp => kvp.Value == "true")
      .Select(kvp => new KeyValuePair<string, string>(kvp.Key[3..], kvp.Value.ToString()))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => p.IsNullable());

    foreach (var property in candidates)
    {
      yield return new IsNullFilter(property.Name, columns[property.Name], $"isNull{property.Name}", tableName);
    }
  }

  public static IEnumerable<FilterDto> GenerateFilterDtos(Type readModelType)
  {
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.IsNullable());

    foreach (var prop in candidates)
    {
      var fieldName = prop.Name;
      var key = $"in-{prop.Name}";
      var description = $"Filters out records where {prop.Name} is null.";

      yield return new FilterDto(fieldName, key, description, ReadModelFilter.FilterSchema(typeof(bool)));
    }
  }
}
