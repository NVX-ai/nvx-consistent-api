using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Nvx.ConsistentAPI;

public record GreaterThanFilter(
  string ColumnName,
  StringValues ParameterValue,
  string ParameterName,
  string TableName,
  PropertyInfo PropertyInfo)
  : ReadModelFilter
{
  private static readonly Type[] CompatibleTypes =
  [
    typeof(int),
    typeof(long),
    typeof(float),
    typeof(double),
    typeof(char),
    typeof(DateTime),
    typeof(decimal),
    typeof(DateOnly),
    typeof(DateTimeOffset)
  ];

  public string WhereClause => $"[{TableName}].[{ColumnName}] > @greaterThan{ColumnName}";

  public Dictionary<string, object> SqlParameters =>
    new() { { ParameterName, ParameterValue.ToParameter(PropertyInfo) } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 3 && kvp.Key.StartsWith("gt-"))
      .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
      .Select(kvp => new KeyValuePair<string, string>(kvp.Key[3..], kvp.Value.ToString()))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => CompatibleTypes.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));

    foreach (var prop in candidates)
    {
      yield return new GreaterThanFilter(prop.Name, columns[prop.Name], $"greaterThan{prop.Name}", tableName, prop);
    }
  }

  public static IEnumerable<FilterDto> GenerateFilterDtos(Type readModelType)
  {
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => CompatibleTypes.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));

    foreach (var property in candidates)
    {
      var fieldName = property.Name;
      var key = $"gt-{property.Name}";
      var description = $"Filters records where {property.Name} is greater than the specified value.";

      yield return new FilterDto(fieldName, key, description, ReadModelFilter.FilterSchema(property.PropertyType));
    }
  }
}
