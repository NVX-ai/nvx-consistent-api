using KurrentDB.Client;
using Nvx.ConsistentAPI.Framework.Serialization;

namespace Nvx.ConsistentAPI.Framework.Events;

public static class ParserBuilder
{
  public static (string, Func<ResolvedEvent, Option<EventModelEvent>>) Build(Type eventType)
  {
    return (Naming.ToSpinalCase(eventType), Parse);

    Option<EventModelEvent> Parse(ResolvedEvent re)
    {
      try
      {
        return EventSerialization
          .Deserialize(re.Event.Data, eventType)
          .Apply(Optional)
          .Map(o => (EventModelEvent)o);
      }
      catch
      {
        return None;
      }
    }
  }
}
