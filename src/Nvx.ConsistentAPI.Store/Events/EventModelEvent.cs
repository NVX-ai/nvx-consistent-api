using Nvx.ConsistentAPI.Store.Events;

namespace Nvx.ConsistentAPI;

public interface EventModelEvent
{
  public string EventType => GetType().Apply(Naming.ToSpinalCase);
  public string GetStreamName() => GetEntityId().StreamName;

  public byte[] ToBytes() => EventSerialization.ToBytes(this);

  StrongId GetEntityId();
}
