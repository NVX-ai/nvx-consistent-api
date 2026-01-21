using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Nvx.ConsistentAPI;

// ReSharper disable once NotAccessedPositionalProperty.Global
public record EnumEqualsFilter(
  string ColumnName,
  StringValues ParameterValue,
  string ParameterName,
  string TableName,
  PropertyInfo PropertyInfo)
  : ReadModelFilter
{
  public string WhereClause => $"[{TableName}].[{ColumnName}] IN ({string.Join(", ", SqlParameters.Keys)}) ";

  public Dictionary<string, object> SqlParameters =>
    ParameterValue
      .Choose(Optional)
      .Apply(p => p.ToArray())
      .Apply(p => p.Length == 1 ? p[0].Split(',').Select(v => v.Trim()).ToArray() : p)
      .Select((v, i) => new KeyValuePair<string, object>($"@enumRangeOf{ColumnName}{i}", v.ToParameter(PropertyInfo)))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 3 && kvp.Key.StartsWith("eq-"))
      .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
      .Select(kvp => new KeyValuePair<string, string[]>(
        kvp.Key[3..],
        kvp.Value.Select(Optional).Choose(Id).ToArray())
      )
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => p.PropertyType.IsEnum);

    foreach (var prop in candidates)
    {
      yield return new EnumEqualsFilter(prop.Name, columns[prop.Name], $"enumRangeOf{prop.Name}", tableName, prop);
    }
  }

  public static IEnumerable<FilterDto> GenerateFilterDtos(Type readModelType)
  {
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.PropertyType.IsEnum);

    foreach (var property in candidates)
    {
      var fieldName = property.Name;
      var key = $"eq-{property.Name}";
      var description = $"Filters records where {property.Name} is equal to the given value.";

      yield return new FilterDto(
        fieldName,
        key,
        description,
        ReadModelFilter.FilterSchema(property.PropertyType, true));
    }
  }
}
