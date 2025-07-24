namespace Nvx.ConsistentAPI.Store.Store;

public readonly struct SubscribeAllRequest()
{
  public readonly ulong? Position = 0;
  public readonly string[] Swimlanes = [];

  public SubscribeAllRequest(ulong? position, string[] swimlanes) : this()
  {
    Position = position;
    Swimlanes = swimlanes;
  }

  public static SubscribeAllRequest Start(params string[] swimlanes) => new(0, swimlanes);
  public static SubscribeAllRequest FromNowOn(params string[] swimlanes) => new(null, swimlanes);

  public static SubscribeAllRequest After(ulong position, params string[] swimlanes) =>
    new(position, swimlanes);

  public SubscribeAllRequest(string[] swimlanes) : this(0, swimlanes) { }
  public SubscribeAllRequest(ulong position) : this(position, []) { }
}
