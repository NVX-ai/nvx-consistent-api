﻿using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Nvx.ConsistentAPI.Framework;

public static class EventSerialization
{
  private static readonly JsonSerializerSettings EventSettings = new()
  {
    DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
    DateFormatHandling = DateFormatHandling.IsoDateFormat,
    DateParseHandling = DateParseHandling.DateTimeOffset
  };

  internal static byte[] ToBytes<T>(T obj) =>
    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj, EventSettings));

  internal static T? Deserialize<T>(byte[] data) => Deserialize<T>(Encoding.UTF8.GetString(data));

  internal static object? Deserialize(ReadOnlyMemory<byte> data, Type type) =>
    JsonConvert.DeserializeObject(Encoding.UTF8.GetString(data.ToArray()), type, EventSettings);

  private static T? Deserialize<T>(string data) => JsonConvert.DeserializeObject<T>(data, EventSettings);
}

public static class Serialization
{
  private static readonly JsonSerializerSettings ExternalSettings = GetExternalSettings();

  private static JsonSerializerSettings GetExternalSettings()
  {
    var settings = new JsonSerializerSettings
    {
      DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
      DateFormatHandling = DateFormatHandling.IsoDateFormat,
      DateParseHandling = DateParseHandling.DateTimeOffset,
      ContractResolver = new DefaultContractResolver
      {
        NamingStrategy = new CamelCaseNamingStrategy()
      }
    };
    settings.Converters.Add(new StringEnumConverter());
    return settings;
  }

  internal static string Serialize<T>(T obj) => JsonConvert.SerializeObject(obj, ExternalSettings);

  internal static T? Deserialize<T>(string body) => JsonConvert.DeserializeObject<T>(body, ExternalSettings);

  internal static object? Deserialize(Type type, string body) =>
    JsonConvert.DeserializeObject(body, type, ExternalSettings);

  internal static async Task<Result<(string body, T? result), string>> Deserialize<T>(Stream stream)
  {
    try
    {
      using var reader = new StreamReader(stream);
      var body = await reader.ReadToEndAsync();
      return (body, JsonConvert.DeserializeObject<T>(body, ExternalSettings));
    }
    catch (JsonSerializationException e)
    {
      return $"Failed to deserialize: {e.Path}";
    }
  }
}
