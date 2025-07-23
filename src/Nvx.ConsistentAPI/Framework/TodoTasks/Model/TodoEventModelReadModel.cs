using System.ComponentModel.DataAnnotations;
using EventStore.Client;
using Nvx.ConsistentAPI.Framework;

namespace Nvx.ConsistentAPI;

public record TodoEventModelReadModel(
  string Id,
  string RelatedEntityId,
  DateTime StartsAt,
  DateTime ExpiresAt,
  [property: MaxLength(StringSizes.Unlimited)]
  string JsonData,
  string Name,
  DateTime? LockedUntil,
  [property: MaxLength(StringSizes.Unlimited)]
  string? SerializedRelatedEntityId,
  Position? EventPosition,
  int RetryCount) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongGuid(Guid.Parse(Id));

  public string GetEntityId() => Id;
}
