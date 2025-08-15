namespace Nvx.ConsistentAPI.Store.Store;

public abstract record InsertionFailure
{
  public record ConsistencyCheckFailed : InsertionFailure;

  public record InsertionFailed : InsertionFailure;

  public record PayloadTooLarge : InsertionFailure;

  public T Match<T>(
    Func<T> consistencyCheckFailed,
    Func<T> insertionFailed,
    Func<T> payloadTooLarge) =>
    this switch
    {
      ConsistencyCheckFailed => consistencyCheckFailed(),
      InsertionFailed => insertionFailed(),
      PayloadTooLarge => payloadTooLarge(),
      _ => insertionFailed()
    };
}

public record InsertionSuccess(ulong GlobalPosition, long StreamPosition);
