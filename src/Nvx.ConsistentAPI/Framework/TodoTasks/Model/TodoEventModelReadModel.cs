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

  public static readonly EventModelingReadModelArtifact Definition =
    new ReadModelDefinition<TodoEventModelReadModel, ProcessorEntity>
    {
      StreamPrefix = ProcessorEntity.StreamPrefix,
      Projector = entity =>
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
          entity.AttemptCount >= ProcessorEntity.MaxAttempts
        )
      ],
      IsExposed = false,
      AreaTag = OperationTags.FrameworkManagement
    };
}
