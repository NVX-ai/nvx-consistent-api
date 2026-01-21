namespace Nvx.ConsistentAPI.Idempotency;

public enum IdempotentRequestState
{
  New = 0,
  Pending = 1,
  Accepted = 2,
  Rejected = 3
}
