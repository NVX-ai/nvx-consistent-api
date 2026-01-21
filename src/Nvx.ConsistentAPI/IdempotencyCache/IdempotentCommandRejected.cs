namespace Nvx.ConsistentAPI.Idempotency;

public record IdempotentCommandRejected(string Key, ApiError Error) : EventModelEvent
{
  public string GetStreamName() => IdempotencyCache.GetStreamName(Key);
  public StrongId GetEntityId() => new StrongString(Key);
}
