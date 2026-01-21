using System.Reflection;
using Microsoft.Extensions.Primitives;

namespace Nvx.ConsistentAPI;

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
