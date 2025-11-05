using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

public partial record ProcessorEntity(
  Guid Id,
  DateTime StartsAt,
  DateTime ExpiresAt,
  DateTime? LockedUntil,
  DateTime? CompletedAt,
  string RelatedEntityId,
  string JsonData,
  string Type,
  string? SerializedRelatedEntityId,
  Position? EventPosition,
  int AttemptCount,
  int TimesWaitedForReadModel
) : EventModelEntity<ProcessorEntity>,
  Folds<TodoCreated, ProcessorEntity>,
  Folds<TodoLockRequested, ProcessorEntity>,
  Folds<TodoCompleted, ProcessorEntity>,
  Folds<TodoLockReleased, ProcessorEntity>,
  Folds<TodoModelSnapshot, ProcessorEntity>,
  Folds<TodoHadDependingReadModelBehind, ProcessorEntity>
{
  public const string StreamPrefix = "framework-todo-processor-";
  public const int MaxAttempts = 5;

  public static readonly EventModel Get =
    new()
    {
      Entities =
      [
        new EntityDefinition<ProcessorEntity, StrongGuid>
        {
          Defaulter = Defaulted,
          StreamPrefix = StreamPrefix,
          CacheSize = 8196
        }
      ],
      ReadModels =
      [
        new ReadModelDefinition<TodoEventModelReadModel, ProcessorEntity>
        {
          StreamPrefix = StreamPrefix,
          Projector = entity =>
            !entity.CompletedAt.HasValue
              ?
              [
                new TodoEventModelReadModel(
                  entity.Id.ToString(),
                  entity.RelatedEntityId,
                  entity.StartsAt,
                  entity.ExpiresAt,
                  entity.CompletedAt,
                  entity.JsonData,
                  entity.Type,
                  entity.LockedUntil,
                  entity.SerializedRelatedEntityId,
                  entity.EventPosition?.CommitPosition,
                  entity.AttemptCount,
                  entity.AttemptCount >= MaxAttempts
                )
              ]
              : [],
          ShouldHydrate = (entity, isBackwards) =>
            DateTime.UtcNow < entity.ExpiresAt || (isBackwards && entity.CompletedAt.HasValue),
          IsExposed = false,
          AreaTag = OperationTags.FrameworkManagement
        }
      ],
      Projections = [new TodoProcessorCompletedToSnapshot()]
    };

  public AsyncResult<LockAvailable, TodoOutcome> LockState =>
    (LockedUntil.HasValue && DateTime.UtcNow < LockedUntil) || CompletedAt is not null
      ? Error<LockAvailable, TodoOutcome>(TodoOutcome.Locked)
      : Ok(new LockAvailable());

  public string GetStreamName() => $"{StreamPrefix}{Id}";

  public ValueTask<ProcessorEntity> Fold(
    TodoCompleted tc,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { CompletedAt = tc.CompletedAt });

  public ValueTask<ProcessorEntity> Fold(TodoCreated tc, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        StartsAt = tc.StartsAt,
        ExpiresAt = tc.ExpiresAt,
        JsonData = tc.JsonData,
        Type = tc.Type,
        RelatedEntityId = tc.RelatedEntityId,
        SerializedRelatedEntityId = tc.SerializedRelatedEntityId,
        EventPosition = metadata.Position
      });

  public ValueTask<ProcessorEntity> Fold(
    TodoHadDependingReadModelBehind evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        LockedUntil = metadata.CreatedAt
                      + TimeSpan.FromSeconds(Math.Min((TimesWaitedForReadModel + 1) * 5, 60_000 * 15)),
        TimesWaitedForReadModel = TimesWaitedForReadModel + 1
      });

  public ValueTask<ProcessorEntity> Fold(
    TodoLockReleased _,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { LockedUntil = null });

  public ValueTask<ProcessorEntity> Fold(
    TodoLockRequested lr,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      lr.RequestedAt > (LockedUntil ?? DateTime.UnixEpoch) && CompletedAt is null
        ? this with
        {
          LockedUntil = lr.RequestedAt + lr.Length,
          AttemptCount = AttemptCount + 1
        }
        : this);

  public ValueTask<ProcessorEntity> Fold(TodoModelSnapshot evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        StartsAt = evt.StartsAt,
        ExpiresAt = evt.ExpiresAt,
        LockedUntil = evt.LockedUntil,
        CompletedAt = evt.CompletedAt,
        RelatedEntityId = evt.RelatedEntityId,
        JsonData = evt.JsonData,
        Type = evt.Type,
        SerializedRelatedEntityId = evt.SerializedRelatedEntityId,
        EventPosition = evt.EventPosition
      });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";

  public static ProcessorEntity Defaulted(StrongGuid id) =>
    new(
      id.Value,
      DateTime.UnixEpoch,
      DateTime.UnixEpoch,
      null,
      null,
      Guid.Empty.ToString(),
      string.Empty,
      string.Empty,
      string.Empty,
      Position.Start,
      0,
      0
    );

  public class LockAvailable
  {
    internal LockAvailable() { }
  }
}
