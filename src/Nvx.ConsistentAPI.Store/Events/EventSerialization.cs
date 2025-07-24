using System.Text;
using Newtonsoft.Json;

namespace Nvx.ConsistentAPI.Store.Events;

public static class EventSerialization
{
  private static readonly JsonSerializerSettings EventSettings = new()
  {
    DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
    DateFormatHandling = DateFormatHandling.IsoDateFormat,
    DateParseHandling = DateParseHandling.DateTimeOffset
  };

  public static byte[] ToBytes<T>(T obj) =>
    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj, EventSettings));

  public static T? Deserialize<T>(byte[] data) => Deserialize<T>(Encoding.UTF8.GetString(data));

  public static object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
    JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data.ToArray()), type, EventSettings);

  private static T? Deserialize<T>(string data) => JsonConvert.DeserializeObject<T>(data, EventSettings);
}
