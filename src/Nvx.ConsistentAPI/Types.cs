using System.Reflection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nvx.ConsistentAPI;

public static class Types
{
  private static readonly NullabilityInfoContext Context = new();

  private static readonly Dictionary<Type, PropertyInfo[]> NonNullableProperties = new();
  private static readonly SemaphoreSlim Semaphore = new(1);

  public static string[] GetNullabilityViolations<T>(T obj)
  {
    var type = typeof(T);
    if (NonNullableProperties.TryGetValue(type, out var value))
    {
      return value
        .Where(p => p.GetValue(obj) == null)
        .Select(p => $"{p.Name} was null")
        .ToArray();
    }

    Semaphore.Wait(TimeSpan.FromMilliseconds(500));
    NonNullableProperties[type] = type
      .GetProperties()
      .Where(p => Context.Create(p).WriteState is not NullabilityState.Nullable)
      .ToArray();
    Semaphore.Release();
    return NonNullableProperties[type]
      .Where(p => p.GetValue(obj) == null)
      .Select(p => $"{p.Name} was null")
      .ToArray();
  }

  public static bool IsNullable(this PropertyInfo prop) =>
    prop.PropertyType.IsValueType
      ? Nullable.GetUnderlyingType(prop.PropertyType) != null
      : !prop.IsNonNullableReferenceType();

  public static EventModelEvent[] ToEventArray<T>(this T evt) where T : EventModelEvent => [evt];
}
