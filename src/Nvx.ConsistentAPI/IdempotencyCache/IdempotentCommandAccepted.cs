namespace Nvx.ConsistentAPI.Idempotency;

public record IdempotentCommandAccepted(string Key, CommandAcceptedResult Accepted) : EventModelEvent
{
  public string GetStreamName() => IdempotencyCache.GetStreamName(Key);
  public StrongId GetEntityId() => new StrongString(Key);
}
