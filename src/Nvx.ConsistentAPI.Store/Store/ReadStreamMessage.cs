namespace Nvx.ConsistentAPI.Store.Store;

public abstract record ReadStreamMessage<EventInterface>
{
  public record ReadingStarted : ReadStreamMessage<EventInterface>;

  public record SolvedEvent(string Swimlane, StrongId Id, EventInterface Event, StoredEventMetadata Metadata)
    : ReadStreamMessage<EventInterface>;

  public record ToxicEvent(
    string Swimlane,
    StrongId? Id,
    byte[] Event,
    byte[] Metadata,
    ulong GlobalPosition,
    ulong StreamPosition)
    : ReadStreamMessage<EventInterface>;

  public record Checkpoint(ulong GlobalPosition) : ReadStreamMessage<EventInterface>;

  public record Terminated(Exception Exception) : ReadStreamMessage<EventInterface>;
}
