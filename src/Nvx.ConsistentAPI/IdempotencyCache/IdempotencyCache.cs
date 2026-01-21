namespace Nvx.ConsistentAPI.Idempotency;

public partial record IdempotencyCache(
  string Key,
  IdempotentRequestState State,
  CommandAcceptedResult? StoredSuccess,
  ApiError? StoredError,
  DateTime? LockedUntil)
  : EventModelEntity<IdempotencyCache>,
    Folds<IdempotentCommandStarted, IdempotencyCache>,
    Folds<IdempotentCommandAccepted, IdempotencyCache>,
    Folds<IdempotentCommandRejected, IdempotencyCache>
{
  public const string StreamPrefix = "idempotency-cache-";

  public static readonly EventModel Get = new()
  {
    Entities =
    [
      new EntityDefinition<IdempotencyCache, StrongString> { Defaulter = Defaulted, StreamPrefix = StreamPrefix }
    ]
  };

  public string GetStreamName() => GetStreamName(Key);

  public ValueTask<IdempotencyCache> Fold(
    IdempotentCommandAccepted evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with { State = IdempotentRequestState.Accepted, LockedUntil = null, StoredSuccess = evt.Accepted });

  public ValueTask<IdempotencyCache> Fold(
    IdempotentCommandRejected evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with { State = IdempotentRequestState.Rejected, LockedUntil = null, StoredError = evt.Error });

  public ValueTask<IdempotencyCache> Fold(
    IdempotentCommandStarted evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with { State = IdempotentRequestState.Pending, LockedUntil = evt.StartedAt.AddSeconds(5) });

  public static string GetStreamName(string key) => $"{StreamPrefix}{key}";

  public static IdempotencyCache Defaulted(StrongString id) =>
    new(id.Value, IdempotentRequestState.New, null, null, null);
}
