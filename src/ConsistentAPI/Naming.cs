using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ConsistentAPI;

public static partial class Naming
{
  private static readonly ConcurrentDictionary<string, string> Cache = new();

  public static string ToSpinalCase<T>() => ToSpinalCase(typeof(T));

  public static string ToSpinalCase(Type t) =>
    Cache.GetOrAdd(
      t.Name,
      _ => MyRegex()
        .Replace(t.Name, "-$1")
        .ToLower()
        .Replace("-read-model", string.Empty));

  [GeneratedRegex("(?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z])")]
  private static partial Regex MyRegex();
}
