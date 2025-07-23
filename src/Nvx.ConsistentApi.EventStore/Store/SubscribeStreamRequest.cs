namespace Nvx.ConsistentAPI.EventStore.Store;

public record SubscribeStreamRequest(string Swimlane, StrongId Id, bool IsFromStart)
{
  public static SubscribeStreamRequest FromStart(string swimlane, StrongId id) =>
    new(swimlane, id, true);

  public static SubscribeStreamRequest FromNowOn(string swimlane, StrongId id) =>
    new(swimlane, id, false);
}
