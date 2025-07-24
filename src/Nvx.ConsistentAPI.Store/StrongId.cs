namespace Nvx.ConsistentAPI;

public abstract record StrongId
{
  public abstract string SwimLane { get; }
  public string StreamName => $"{SwimLane}{StreamId()}";
  public abstract string StreamId();
  public abstract override string ToString();
}
