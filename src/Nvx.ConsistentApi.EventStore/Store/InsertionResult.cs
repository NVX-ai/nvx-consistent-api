namespace Nvx.ConsistentAPI.EventStore.Store;

public abstract record InsertionFailure
{
  public record ConsistencyCheckFailed : InsertionFailure;

  public record InsertionFailed : InsertionFailure;

  public record PayloadTooLarge : InsertionFailure;
}

public record InsertionSuccess(ulong GlobalPosition, ulong StreamPosition);
