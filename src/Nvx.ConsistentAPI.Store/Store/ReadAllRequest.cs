namespace Nvx.ConsistentAPI.Store.Store;

public readonly struct ReadAllRequest()
{
  public readonly ulong? Position = null;
  public readonly RelativePosition? Relative;
  public readonly ReadDirection Direction = ReadDirection.Forwards;
  public readonly string[] Swimlanes = [];

  public ReadAllRequest(
    ulong? position,
    RelativePosition? relativePosition,
    ReadDirection direction,
    string[] swimlanes) : this()
  {
    Relative = relativePosition;
    Direction = direction;
    Position = position;
    Swimlanes = swimlanes;
  }

  public static ReadAllRequest Start(string[]? swimlanes = null) => new(
    0,
    RelativePosition.Start,
    ReadDirection.Forwards,
    swimlanes ?? []);

  public static ReadAllRequest End(string[]? swimlanes = null) => new(
    null,
    RelativePosition.End,
    ReadDirection.Backwards,
    swimlanes ?? []);

  public static ReadAllRequest FromAndAfter(ulong position, string[]? swimlanes = null) =>
    new(position, null, ReadDirection.Forwards, swimlanes ?? []);

  public static ReadAllRequest FromAndBefore(ulong position, string[]? swimlanes = null) =>
    new(position, null, ReadDirection.Backwards, swimlanes ?? []);
}
