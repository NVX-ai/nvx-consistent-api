using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Snapshot event that captures the full state of a completed todo for efficient rehydration.
/// </summary>
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
