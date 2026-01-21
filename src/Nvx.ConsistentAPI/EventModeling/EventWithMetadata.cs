using KurrentDB.Client;

namespace Nvx.ConsistentAPI.EventModeling;

public record EventWithMetadata<E>(
  E Event,
  Position Revision,
  Uuid EventId,
  EventMetadata Metadata) where E : EventModelEvent
{
  public EventWithMetadata<E2> As<E2>(E2 e) where E2 : EventModelEvent => new(e, Revision, EventId, Metadata);
}
