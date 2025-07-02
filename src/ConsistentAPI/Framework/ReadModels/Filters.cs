using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace ConsistentAPI;

// ReSharper disable once NotAccessedPositionalProperty.Global
public record FilterDto(string FieldName, string Key, string Description, OpenApiSchema Schema);

public interface ReadModelFilter
{
  delegate IEnumerable<ReadModelFilter> FilterBuilder(
    Type readModelType,
    IQueryCollection parameters,
    string tableName);

  private static readonly IEnumerable<FilterBuilder> AvailableFilters =
  [
    TextSearchFilter.Parse,
    EqualsFilter.Parse,
    EnumEqualsFilter.Parse,
    GreaterThanFilter.Parse,
    GreaterOrEqualThanFilter.Parse,
    LessThanFilter.Parse,
    LessOrEqualThanFilter.Parse,
    IsNullFilter.Parse,
    NotNullFilter.Parse,
    IsInArrayFilter.Parse,
    IsNotInArrayFilter.Parse,
    IsArrayEmptyFilter.Parse,
    IsArrayNotEmptyFilter.Parse,
    TextSearchInArrayFilter.Parse
  ];

  string WhereClause { get; }
  Dictionary<string, object> SqlParameters { get; }

  public static IEnumerable<ReadModelFilter> Get(Type readModelType, IQueryCollection parameters, string tableName) =>
    AvailableFilters.SelectMany(f => f(readModelType, parameters, tableName));

  public static IEnumerable<FilterDto> Get(Type readModelType) =>
    TextSearchFilter
      .GenerateFilterDtos(readModelType)
      .Concat(EqualsFilter.GenerateFilterDtos(readModelType))
      .Concat(EnumEqualsFilter.GenerateFilterDtos(readModelType))
      .Concat(GreaterThanFilter.GenerateFilterDtos(readModelType))
      .Concat(GreaterOrEqualThanFilter.GenerateFilterDtos(readModelType))
      .Concat(LessThanFilter.GenerateFilterDtos(readModelType))
      .Concat(LessOrEqualThanFilter.GenerateFilterDtos(readModelType))
      .Concat(IsNullFilter.GenerateFilterDtos(readModelType))
      .Concat(NotNullFilter.GenerateFilterDtos(readModelType))
      .Concat(IsInArrayFilter.GenerateFilterDtos(readModelType))
      .Concat(IsNotInArrayFilter.GenerateFilterDtos(readModelType))
      .Concat(IsArrayEmptyFilter.GenerateFilterDtos(readModelType))
      .Concat(IsArrayNotEmptyFilter.GenerateFilterDtos(readModelType))
      .Concat(TextSearchInArrayFilter.GenerateFilterDtos(readModelType));

  private static Type UnderlyingType(Type type) =>
    Nullable.GetUnderlyingType(type) ?? type;

  internal static OpenApiSchema FilterSchema(Type propertyType, bool isArray = false) =>
    UnderlyingType(propertyType)
      .Apply<Type, OpenApiSchema>(ut =>
      {
        OpenApiSchema item = ut switch
        {
          not null when ut == typeof(int) => new OpenApiSchema { Type = "integer" },
          not null when ut == typeof(long) => new OpenApiSchema { Type = "integer", Format = "int64" },
          not null when ut == typeof(float) => new OpenApiSchema { Type = "number", Format = "float" },
          not null when ut == typeof(decimal) => new OpenApiSchema { Type = "number", Format = "float" },
          not null when ut == typeof(double) => new OpenApiSchema { Type = "number", Format = "double" },
          not null when ut == typeof(bool) => new OpenApiSchema { Type = "boolean" },
          not null when ut == typeof(string) => new OpenApiSchema { Type = "string" },
          not null when ut == typeof(char) => new OpenApiSchema { Type = "string" },
          not null when ut == typeof(DateTime) => new OpenApiSchema { Type = "datetime" },
          not null when ut == typeof(Guid) => new OpenApiSchema { Type = "string", Format = "uuid" },
          not null when ut.IsEnum => new OpenApiSchema
            { Type = "string", Enum = ut.GetEnumNames().Select(IOpenApiAny (ev) => new OpenApiString(ev)).ToList() },
          _ => new OpenApiSchema { Type = "string" }
        };

        return isArray ? new OpenApiSchema { Type = "array", Items = item } : item;
      });
}

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

// ReSharper disable once NotAccessedPositionalProperty.Global
public record IsArrayEmptyFilter(string ColumnName, StringValues ParameterValue, string ParameterName, string TableName)
  : ReadModelFilter
{
  private readonly string arrayTableName = DatabaseHandler.GetArrayPropTableName(ColumnName, TableName);

  public string WhereClause =>
    $"""
     (
      SELECT COUNT(*)
      FROM [{arrayTableName}] 
      WHERE [{arrayTableName}].[Id] = [{TableName}].[Id]
     ) = 0
     """;

  public Dictionary<string, object> SqlParameters => new() { { ParameterName, ParameterValue } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 4 && kvp.Key.StartsWith("iae-"))
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
      yield return new IsArrayEmptyFilter(
        property.Name,
        columns[property.Name],
        $"isArrayEmpty{property.Name}",
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
      var key = $"iae-{property.Name}";
      var description = $"Filters records where {fieldName} is an empty array.";

      yield return new FilterDto(fieldName, key, description, ReadModelFilter.FilterSchema(typeof(bool)));
    }
  }
}

public record IsNotInArrayFilter(string ColumnName, StringValues ParameterValue, string ParameterName, string TableName)
  : ReadModelFilter
{
  private readonly string arrayTableName = DatabaseHandler.GetArrayPropTableName(ColumnName, TableName);

  public string WhereClause =>
    $"""
     (
      SELECT COUNT(*)
      FROM [{arrayTableName}] 
      WHERE LOWER([{arrayTableName}].[Value]) = LOWER(@notInArray{ColumnName})
        AND [{arrayTableName}].[Id] = [{TableName}].[Id]
     ) = 0
     """;

  public Dictionary<string, object> SqlParameters => new() { { ParameterName, ParameterValue } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 4 && kvp.Key.StartsWith("nia-"))
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
      yield return new IsNotInArrayFilter(
        property.Name,
        columns[property.Name],
        $"notInArray{property.Name}",
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
      var key = $"nia-{property.Name}";
      var description = $"Filters records where {fieldName} is not in the given array.";

      yield return new FilterDto(
        fieldName,
        key,
        description,
        ReadModelFilter.FilterSchema(property.PropertyType, true));
    }
  }
}

public record IsInArrayFilter(string ColumnName, StringValues ParameterValue, string ParameterName, string TableName)
  : ReadModelFilter
{
  private readonly string arrayTableName = DatabaseHandler.GetArrayPropTableName(ColumnName, TableName);

  public string WhereClause =>
    $"""
     (
      SELECT COUNT(*)
      FROM [{arrayTableName}] 
      WHERE LOWER([{arrayTableName}].[Value]) = LOWER(@inArray{ColumnName})
        AND [{arrayTableName}].[Id] = [{TableName}].[Id]
     ) > 0
     """;

  public Dictionary<string, object> SqlParameters => new() { { ParameterName, ParameterValue } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 3 && kvp.Key.StartsWith("ia-"))
      .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
      .Select(kvp => new KeyValuePair<string, string>(kvp.Key[3..], kvp.Value.ToString()))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => p.PropertyType.GetElementType()! == typeof(string)
                  || p.PropertyType.GetElementType()! == typeof(int)
                  || p.PropertyType.GetElementType()! == typeof(Guid));

    foreach (var property in candidates)
    {
      yield return new IsInArrayFilter(property.Name, columns[property.Name], $"inArray{property.Name}", tableName);
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
      var key = $"ia-{fieldName}";
      var description = $"Filters records where {fieldName} is in the given array.";

      yield return new FilterDto(
        fieldName,
        key,
        description,
        ReadModelFilter.FilterSchema(property.PropertyType, true));
    }
  }
}

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

public record GreaterOrEqualThanFilter(
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

  public string WhereClause => $"[{TableName}].[{ColumnName}] >= @biggerOrEqualThan{ColumnName}";

  public Dictionary<string, object> SqlParameters =>
    new() { { ParameterName, ParameterValue.ToParameter(PropertyInfo) } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 4 && kvp.Key.StartsWith("gte-"))
      .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
      .Select(kvp => new KeyValuePair<string, string>(kvp.Key[4..], kvp.Value.ToString()))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => CompatibleTypes.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));

    foreach (var prop in candidates)
    {
      yield return new GreaterOrEqualThanFilter(
        prop.Name,
        columns[prop.Name],
        $"biggerOrEqualThan{prop.Name}",
        tableName,
        prop);
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
      var key = $"gte-{property.Name}";
      var description = $"Filters records where {property.Name} is greater than or equal to the given value.";

      yield return new FilterDto(fieldName, key, description, ReadModelFilter.FilterSchema(property.PropertyType));
    }
  }
}

public record LessThanFilter(
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

  public string WhereClause => $"[{TableName}].[{ColumnName}] < @smallerThan{ColumnName}";

  public Dictionary<string, object> SqlParameters =>
    new() { { ParameterName, ParameterValue.ToParameter(PropertyInfo) } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 3 && kvp.Key.StartsWith("lt-"))
      .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
      .Select(kvp => new KeyValuePair<string, string>(kvp.Key[3..], kvp.Value.ToString()))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => CompatibleTypes.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));

    foreach (var prop in candidates)
    {
      yield return new LessThanFilter(prop.Name, columns[prop.Name], $"smallerThan{prop.Name}", tableName, prop);
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
      var key = $"lt-{property.Name}";
      var description = $"Filters records where {property.Name} is less than the given value.";

      yield return new FilterDto(fieldName, key, description, ReadModelFilter.FilterSchema(property.PropertyType));
    }
  }
}

public record LessOrEqualThanFilter(
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

  public string WhereClause => $"[{TableName}].[{ColumnName}] <= @lessOrEqualThan{ColumnName}";

  public Dictionary<string, object> SqlParameters =>
    new() { { ParameterName, ParameterValue.ToParameter(PropertyInfo) } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 4 && kvp.Key.StartsWith("lte-"))
      .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
      .Select(kvp => new KeyValuePair<string, string>(kvp.Key[4..], kvp.Value.ToString()))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => CompatibleTypes.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));

    foreach (var prop in candidates)
    {
      yield return new LessOrEqualThanFilter(
        prop.Name,
        columns[prop.Name],
        $"lessOrEqualThan{prop.Name}",
        tableName,
        prop);
    }
  }

  public static IEnumerable<FilterDto> GenerateFilterDtos(Type readModelType)
  {
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => CompatibleTypes.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));

    foreach (var prop in candidates)
    {
      var fieldName = prop.Name;
      var key = $"lte-{prop.Name}";
      var description = $"Filters records where {prop.Name} is less than or equal to the given value.";

      yield return new FilterDto(fieldName, key, description, ReadModelFilter.FilterSchema(prop.PropertyType));
    }
  }
}

// ReSharper disable once NotAccessedPositionalProperty.Global
public record EqualsFilter(
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
    typeof(bool),
    typeof(string),
    typeof(char),
    typeof(DateTime),
    typeof(Guid),
    typeof(decimal),
    typeof(DateOnly),
    typeof(DateTimeOffset)
  ];

  public string WhereClause => $"[{TableName}].[{ColumnName}] IN ({string.Join(", ", SqlParameters.Keys)}) ";

  public Dictionary<string, object> SqlParameters =>
    ParameterValue
      .Choose(Optional)
      .Apply(p => p.ToArray())
      .Apply(p => p.Length == 1 ? p[0].Split(',').Select(v => v.Trim()).ToArray() : p)
      .Select((v, i) => new KeyValuePair<string, object>($"@rangeOf{ColumnName}{i}", v.ToParameter(PropertyInfo)))
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
      .Where(p => CompatibleTypes.Contains(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));

    foreach (var prop in candidates)
    {
      yield return new EqualsFilter(prop.Name, columns[prop.Name], $"rangeOf{prop.Name}", tableName, prop);
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

public record NotNullFilter(string ColumnName, StringValues ParameterValue, string ParameterName, string TableName)
  : ReadModelFilter
{
  public string WhereClause => $"[{TableName}].[{ColumnName}] IS NOT NULL";
  public Dictionary<string, object> SqlParameters => new() { { ParameterName, ParameterValue } };

  public static IEnumerable<ReadModelFilter> Parse(Type readModelType, IQueryCollection parameters, string tableName)
  {
    var columns = parameters
      .Where(kvp => kvp.Key.Length > 3 && kvp.Key.StartsWith("nn-"))
      .Where(kvp => kvp.Value == "true")
      .Select(kvp => new KeyValuePair<string, string>(kvp.Key[3..], kvp.Value.ToString()))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    var candidates = readModelType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => columns.Any(c => c.Key == p.Name))
      .Where(p => p.IsNullable());

    foreach (var property in candidates)
    {
      yield return new NotNullFilter(property.Name, columns[property.Name], $"notNull{property.Name}", tableName);
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
      var key = $"nn-{prop.Name}";
      var description = $"Filters out records where {prop.Name} is not null.";

      yield return new FilterDto(fieldName, key, description, ReadModelFilter.FilterSchema(typeof(bool)));
    }
  }
}

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

internal static class FilteringExtensions
{
  public static bool IsDateTimeOffset(this PropertyInfo p) =>
    (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(DateTimeOffset);

  public static object ToParameter(this string value, PropertyInfo? p = null) =>
    p switch
    {
      _ when p?.IsDateTimeOffset() ?? false => DateTimeOffset.Parse(value),
      _ when p?.PropertyType.IsEnum ?? false => Enum.Parse(p.PropertyType, value),
      _ => value
    };

  public static object ToParameter(this StringValues value, PropertyInfo? p = null) =>
    p is not null && p.IsDateTimeOffset() ? DateTimeOffset.Parse(value.ToString()) : value;
}
