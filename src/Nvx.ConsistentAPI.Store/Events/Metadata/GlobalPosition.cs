namespace Nvx.ConsistentAPI.Store.Events.Metadata;


/// <summary>
/// Legacy, meant to mimic the Position from EventStore DB's client for serialization purposes.
/// </summary>
public readonly struct GlobalPosition : IEquatable<GlobalPosition>, IComparable<GlobalPosition>, IComparable
{
  public static readonly GlobalPosition Start = new(0UL, 0UL);
  public static readonly GlobalPosition End = new(ulong.MaxValue, ulong.MaxValue);
  public readonly ulong CommitPosition;
  public readonly ulong PreparePosition;

  public GlobalPosition(ulong commitPosition, ulong preparePosition)
  {
    if (commitPosition < preparePosition)
    {
      throw new ArgumentOutOfRangeException(
        nameof(commitPosition),
        "The commit position cannot be less than the prepare position");
    }

    if (commitPosition > long.MaxValue && commitPosition != ulong.MaxValue)
    {
      throw new ArgumentOutOfRangeException(nameof(commitPosition));
    }

    if (preparePosition > long.MaxValue && preparePosition != ulong.MaxValue)
    {
      throw new ArgumentOutOfRangeException(nameof(preparePosition));
    }

    CommitPosition = commitPosition;
    PreparePosition = preparePosition;
  }

  public static bool operator <(GlobalPosition p1, GlobalPosition p2)
  {
    if (p1.CommitPosition < p2.CommitPosition)
    {
      return true;
    }

    return (long)p1.CommitPosition == (long)p2.CommitPosition && p1.PreparePosition < p2.PreparePosition;
  }

  public static bool operator >(GlobalPosition p1, GlobalPosition p2)
  {
    if (p1.CommitPosition > p2.CommitPosition)
    {
      return true;
    }

    return (long)p1.CommitPosition == (long)p2.CommitPosition && p1.PreparePosition > p2.PreparePosition;
  }

  public static bool operator >=(GlobalPosition p1, GlobalPosition p2) => p1 > p2 || p1 == p2;

  public static bool operator <=(GlobalPosition p1, GlobalPosition p2) => p1 < p2 || p1 == p2;

  public static bool operator ==(GlobalPosition p1, GlobalPosition p2) => Equals(p1, p2);

  public static bool operator !=(GlobalPosition p1, GlobalPosition p2) => !(p1 == p2);

  public int CompareTo(GlobalPosition other)
  {
    if (this == other)
    {
      return 0;
    }

    return !(this > other) ? -1 : 1;
  }

  public int CompareTo(object? obj)
  {
    if (obj == null)
    {
      return 1;
    }

    if (obj is GlobalPosition other)
    {
      return CompareTo(other);
    }

    throw new ArgumentException("Object is not a Position");
  }

  public override bool Equals(object? obj) => obj is GlobalPosition other && Equals(other);

  public bool Equals(GlobalPosition other) => (long)CommitPosition == (long)other.CommitPosition
                                              && (long)PreparePosition == (long)other.PreparePosition;

  public override int GetHashCode() => HashCode.Combine(HashCode.Combine(CommitPosition), PreparePosition);

  public override string ToString() => $"C:{CommitPosition}/P:{PreparePosition}";

  public static bool TryParse(string value, out GlobalPosition? position)
  {
    position = new GlobalPosition?();
    var strArray = value.Split('/');
    ulong p1;
    ulong p2;
    if (strArray.Length != 2
        || !TryParsePosition("C", strArray[0], out p1)
        || !TryParsePosition("P", strArray[1], out p2))
    {
      return false;
    }

    position = new GlobalPosition(p1, p2);
    return true;

    static bool TryParsePosition(string expectedPrefix, string v, out ulong p)
    {
      p = 0UL;
      var strArray = v.Split(':');
      return strArray.Length == 2 && strArray[0] == expectedPrefix && ulong.TryParse(strArray[1], out p);
    }
  }
}
