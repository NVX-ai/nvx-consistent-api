namespace Nvx.ConsistentAPI.Store.Store;

public abstract record ReadAllMessage<EventInterface>
{
  public record ReadingStarted : ReadAllMessage<EventInterface>;

  public record AllEvent(string Swimlane, StrongId StrongId, EventInterface Event, StoredEventMetadata Metadata) : ReadAllMessage<EventInterface>;

  public record ToxicAllEvent(
    string Swimlane,
    string InlinedStrongId,
    byte[] EventMetadata,
    ulong GlobalPosition,
    ulong StreamPosition) : ReadAllMessage<EventInterface>;

  public record Checkpoint(ulong GlobalPosition) : ReadAllMessage<EventInterface>;

  public record Terminated(Exception Exception) : ReadAllMessage<EventInterface>;
}
