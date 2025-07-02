using EventStore.Client;

namespace ConsistentAPI;

public record TodoEventModelReadModel(
  string Id,
  string RelatedEntityId,
  DateTime StartsAt,
  DateTime ExpiresAt,
  string JsonData,
  string Name,
  DateTime? LockedUntil,
  string? SerializedRelatedEntityId,
  Position? EventPosition,
  int RetryCount) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongGuid(Guid.Parse(Id));

  public string GetEntityId() => Id;
}
