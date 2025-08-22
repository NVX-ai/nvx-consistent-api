using Nvx.ConsistentAPI.Store.Events;

namespace Nvx.ConsistentAPI;

public interface EventModelEvent : HasSwimlane, HasEntityId
{
  public string EventType => GetType().Apply(Naming.ToSpinalCase);
  public string GetStreamName() => $"{GetSwimlane()}{GetEntityId().StreamId()}";
}
