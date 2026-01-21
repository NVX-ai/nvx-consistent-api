using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace Nvx.ConsistentAPI;

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
        var item = ut switch
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
