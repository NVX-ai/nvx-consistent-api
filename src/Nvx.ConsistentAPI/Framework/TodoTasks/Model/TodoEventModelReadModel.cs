using System.ComponentModel.DataAnnotations;
using EventStore.Client;
using Nvx.ConsistentAPI.Framework;

namespace Nvx.ConsistentAPI;

public record TodoEventModelReadModel(
  string Id,
  string RelatedEntityId,
  DateTime StartsAt,
  DateTime ExpiresAt,
  DateTime? CompletedAt,
  [property: MaxLength(StringSizes.Unlimited)]
  string JsonData,
  string Name,
  DateTime? LockedUntil,
  [property: MaxLength(StringSizes.Unlimited)]
  string? SerializedRelatedEntityId,
  ulong? EventPosition,
  int RetryCount,
  bool IsFailed) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongGuid(Guid.Parse(Id));

  public string GetEntityId() => Id;
}
