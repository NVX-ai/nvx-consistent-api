namespace Nvx.ConsistentAPI.EventStore.Store;

public abstract record ReadAllMessage
{
  public record ReadingStarted : ReadAllMessage;

  public record AllEvent(string Swimlane, StrongId StrongId, StoredEventMetadata Metadata) : ReadAllMessage;

  public record ToxicAllEvent(
    string Swimlane,
    string InlinedStrongId,
    byte[] EventMetadata,
    ulong GlobalPosition,
    ulong StreamPosition) : ReadAllMessage;

  public record Checkpoint(ulong GlobalPosition) : ReadAllMessage;

  public record Terminated(Exception Exception) : ReadAllMessage;
}
