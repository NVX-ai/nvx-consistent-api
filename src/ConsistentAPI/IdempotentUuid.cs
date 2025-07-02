using System.Security.Cryptography;
using System.Text;
using EventStore.Client;

namespace ConsistentAPI;

public static class IdempotentUuid
{
  public static Uuid Generate(string input)
  {
    if (string.IsNullOrEmpty(input))
    {
      throw new ArgumentException("Must have a value", nameof(input));
    }

    var hashedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));

    // Make the UUID RFC4122 compliant (variant and version)
    hashedBytes[6] = (byte)((hashedBytes[6] & 0x0F) | 0x40); // version 4
    hashedBytes[8] = (byte)((hashedBytes[8] & 0x3F) | 0x80); // variant 1

    return Uuid.FromInt64(
      BitConverter.ToInt64(hashedBytes, 0),
      BitConverter.ToInt64(hashedBytes, 8)
    );
  }
}
