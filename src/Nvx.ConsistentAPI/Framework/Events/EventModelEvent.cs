using Nvx.ConsistentAPI.Framework.Serialization;

namespace Nvx.ConsistentAPI.Framework.Events;

public interface EventModelSnapshotEvent : EventModelEvent;

public interface EventModelEvent
{
  public string EventType => GetType().Apply(Naming.ToSpinalCase);
  string GetStreamName();
  public byte[] ToBytes() => EventSerialization.ToBytes(this);
  StrongId GetEntityId();
  public bool ShouldTriggerHydration(EventMetadata metadata, bool isBackwards) => true;
}
