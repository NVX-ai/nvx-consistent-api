using System.Text;
using Newtonsoft.Json;

namespace Nvx.ConsistentAPI;

public static class EventModelEventSerialization
{
  private static readonly Type[] EventTypes =
    AppDomain
      .CurrentDomain.GetAssemblies()
      .SelectMany(a => a.GetTypes())
      .Where(t => t is { IsClass: true, IsAbstract: false })
      .Where(t => t.GetInterfaces().Any(i => i == typeof(EventModelEvent)))
      .ToArray();

  public static Option<(EventModelEvent evt, StrongId streamId)> Deserialize(string eventType, byte[] bytes)
  {
    try
    {
      return EventTypes
        .FirstOrNone(t => t.Name == eventType)
        .Bind(t => Optional(JsonConvert.DeserializeObject(Encoding.UTF8.GetString(bytes), t) as EventModelEvent))
        .Map(e => (e, e.GetEntityId()));
    }
    catch
    {
      return None;
    }
  }

  public static (string typeName, byte[] data) Serialize(EventModelEvent evt) => (evt.GetType().Name,
    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(evt)));
}
