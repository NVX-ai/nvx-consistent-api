using System.Security.Cryptography;
using System.Text;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nvx.ConsistentAPI;

public static class TypeHasher
{
  public static string ComputeTypeHash(Type type) =>
    ComputeTypeHashRecursive(type, type.FullName ?? type.Name, new HashSet<Type>());

  private static string ComputeTypeHashRecursive(
    Type type,
    string propertyName,
    ISet<Type> visitedTypes)
  {
    if (!visitedTypes.Add(type))
    {
      return $"{propertyName}-{type.FullName ?? type.Name}";
    }

    var typeNameBytes = Encoding.UTF8.GetBytes($"{propertyName}-{type.FullName ?? type.Name}");
    var hashBytes = SHA256.HashData(typeNameBytes);

    foreach (var property in type.GetProperties())
    {
      var propertyType = property.PropertyType;
      var isNullable =
        propertyType.IsValueType
          ? Nullable.GetUnderlyingType(propertyType) != null
          : !property.IsNonNullableReferenceType();
      var propertyHashBytes = Encoding.UTF8.GetBytes(
        $"{property.Name}-{propertyType.FullName ?? propertyType.Name}-nullable-{isNullable}"
      );
      hashBytes = Combine(hashBytes, propertyHashBytes);

      if (propertyType.IsGenericType)
      {
        var count = 0;
        hashBytes = propertyType
          .GetGenericArguments()
          .Aggregate(
            hashBytes,
            (current, t) => Combine(current, t, $"{property.Name}-generic-{count++}", visitedTypes)
          );
      }

      if (propertyType.IsPrimitive || propertyType == typeof(string))
      {
        continue;
      }

      hashBytes = Combine(hashBytes, propertyType, property.Name, visitedTypes);
    }

    return ToHexadecimalString(RehashTo32Bytes(hashBytes));
  }

  private static byte[] Combine(IEnumerable<byte> array1, Type type, string propertyName, ISet<Type> visitedTypes) =>
    array1.Concat(Encoding.UTF8.GetBytes(ComputeTypeHashRecursive(type, propertyName, visitedTypes))).ToArray();

  private static byte[] Combine(IEnumerable<byte> array1, IEnumerable<byte> array2) =>
    array1.Concat(array2).ToArray();

  private static byte[] RehashTo32Bytes(byte[] input)
  {
    if (input.Length == 32)
    {
      return input;
    }

    var hash = SHA256.HashData(input);

    if (hash.Length >= 32)
    {
      return hash.Take(32).ToArray();
    }

    var result = new byte[32];
    Array.Copy(hash, 0, result, 0, hash.Length);
    return result;
  }

  private static string ToHexadecimalString(byte[] bytes) =>
    BitConverter.ToString(bytes).Replace("-", "").ToLower();
}
