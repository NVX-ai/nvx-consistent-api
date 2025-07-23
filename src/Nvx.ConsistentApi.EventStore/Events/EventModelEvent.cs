using Nvx.ConsistentAPI.EventStore.Events;

namespace Nvx.ConsistentAPI;

public interface EventModelEvent
{
  public string EventType => GetType().Apply(Naming.ToSpinalCase);
  string GetStreamName();

  public byte[] ToBytes() => EventSerialization.ToBytes(this);

  StrongId GetEntityId();
}
