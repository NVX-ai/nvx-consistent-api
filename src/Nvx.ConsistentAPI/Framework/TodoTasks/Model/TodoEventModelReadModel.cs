using System.ComponentModel.DataAnnotations;
using EventStore.Client;
using Nvx.ConsistentAPI.Framework;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Read model representing an active (uncompleted) todo task, projected from
/// <see cref="ProcessorEntity"/> state into a SQL table for querying by the <see cref="TodoRepository"/>.
/// </summary>
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
