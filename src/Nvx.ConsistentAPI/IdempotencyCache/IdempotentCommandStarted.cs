namespace Nvx.ConsistentAPI.Idempotency;

public record IdempotentCommandStarted(string Key, DateTime StartedAt) : EventModelEvent
{
  public string GetStreamName() => IdempotencyCache.GetStreamName(Key);
  public StrongId GetEntityId() => new StrongString(Key);
}
