using Nvx.ConsistentAPI.Store.Events;

namespace Nvx.ConsistentAPI;

public interface EventModelEvent
{
  public string EventType => GetType().Apply(Naming.ToSpinalCase);
  public string GetStreamName() => $"{GetSwimlane()}{GetEntityId().StreamId()}";
  string GetSwimlane();
  public byte[] ToBytes() => EventSerialization.ToBytes(this);
  StrongId GetEntityId();
}
