using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

public record TodoCreated(
  Guid Id,
  DateTime StartsAt,
  DateTime ExpiresAt,
  string JsonData,
  string Type,
  string RelatedEntityId,
  string? SerializedRelatedEntityId) : EventModelEvent
{
  public string GetStreamName() => ProcessorEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record TodoLockRequested(Guid Id, DateTime RequestedAt, TimeSpan Length) : EventModelEvent
{
  public string GetStreamName() => ProcessorEntity.GetStreamName(Id);

  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record TodoLockReleased(Guid Id) : EventModelEvent
{
  public string GetStreamName() => ProcessorEntity.GetStreamName(Id);

  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record TodoCompleted(Guid Id, DateTime CompletedAt) : EventModelEvent
{
  public string GetStreamName() => ProcessorEntity.GetStreamName(Id);

  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record TodoHadDependingReadModelBehind(Guid Id) : EventModelEvent
{
  public string GetStreamName() => ProcessorEntity.GetStreamName(Id);

  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record TodoModelSnapshot(
  Guid Id,
  DateTime StartsAt,
  DateTime ExpiresAt,
  DateTime? LockedUntil,
  DateTime? CompletedAt,
  string RelatedEntityId,
  string JsonData,
  string Type,
  string? SerializedRelatedEntityId,
  Position? EventPosition) : EventModelSnapshotEvent
{
  public string GetStreamName() => ProcessorEntity.GetStreamName(Id);

  public StrongId GetEntityId() => new StrongGuid(Id);
  public bool ShouldTriggerHydration(EventMetadata metadata, bool isBackwards) => !isBackwards;
}
