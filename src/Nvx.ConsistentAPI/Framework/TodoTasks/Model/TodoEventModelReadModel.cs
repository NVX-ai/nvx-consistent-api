using System.ComponentModel.DataAnnotations;
using EventStore.Client;

namespace Nvx.ConsistentAPI;

public record TodoEventModelReadModel(
  string Id,
  string RelatedEntityId,
  DateTime StartsAt,
  DateTime ExpiresAt,
  [property: MaxLength(int.MaxValue)]
  string JsonData,
  string Name,
  DateTime? LockedUntil,
  [property: MaxLength(int.MaxValue)]
  string? SerializedRelatedEntityId,
  Position? EventPosition,
  int RetryCount) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongGuid(Guid.Parse(Id));

  public string GetEntityId() => Id;
}
