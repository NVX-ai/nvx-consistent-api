namespace Nvx.ConsistentAPI;

public abstract record StrongId
{
  public abstract string StreamId();
  public abstract override string ToString();
}
