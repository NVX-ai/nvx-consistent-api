using Nvx.ConsistentAPI.Store.Events;

namespace Nvx.ConsistentAPI;

public interface EventModelEvent
{
  public string EventType => GetType().Apply(Naming.ToSpinalCase);
  public string GetStreamName() => $"{SwimLane}{GetEntityId().StreamId()}";
  string SwimLane { get; }
  public byte[] ToBytes() => EventSerialization.ToBytes(this);
  StrongId GetEntityId();
}
