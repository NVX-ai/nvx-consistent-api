namespace Nvx.ConsistentAPI.Store.Store;

public record ReadStreamRequest(
  string Swimlane,
  StrongId Id,
  RelativePosition? Position = null,
  ReadDirection Direction = ReadDirection.Forwards,
  ulong? StreamPosition = null)
{
  public static ReadStreamRequest Forwards(string swimlane, StrongId id) => new(swimlane, id, RelativePosition.Start);

  public static ReadStreamRequest FromAndAfter(string swimlane, StrongId id, ulong streamPosition) =>
    new(swimlane, id, null, ReadDirection.Forwards, streamPosition);

  public static ReadStreamRequest FromAndBefore(string swimlane, StrongId id, ulong streamPosition) =>
    new(swimlane, id, null, ReadDirection.Backwards, streamPosition);

  public static ReadStreamRequest Backwards(string swimlane, StrongId id) =>
    new(swimlane, id, RelativePosition.End, ReadDirection.Backwards);
}
