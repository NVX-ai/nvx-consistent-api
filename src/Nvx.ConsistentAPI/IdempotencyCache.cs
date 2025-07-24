namespace Nvx.ConsistentAPI;

public enum IdempotentRequestState
{
  New = 0,
  Pending = 1,
  Accepted = 2,
  Rejected = 3
}

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

public record IdempotentCommandStarted(string Key, DateTime StartedAt) : EventModelEvent
{
  public string SwimLane => IdempotencyCache.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Key);
}

public record IdempotentCommandAccepted(string Key, CommandAcceptedResult Accepted) : EventModelEvent
{
  public string SwimLane => IdempotencyCache.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Key);
}

public record IdempotentCommandRejected(string Key, ApiError Error) : EventModelEvent
{
  public string SwimLane => IdempotencyCache.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Key);
}
