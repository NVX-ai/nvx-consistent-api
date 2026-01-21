namespace Nvx.ConsistentAPI.Framework.Entities;

public abstract record StrongId
{
  public abstract string StreamId();
  public abstract override string ToString();
}

public record StrongString(string Value) : StrongId
{
  public override string StreamId() => Value;
  public override string ToString() => StreamId();
}

public record StrongGuid(Guid Value) : StrongId
{
  public override string StreamId() => Value.ToString();
  public override string ToString() => StreamId();
}
