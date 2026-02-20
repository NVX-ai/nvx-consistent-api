namespace Nvx.ConsistentAPI;

/// <summary>
/// Event emitted when a new todo task is created from a source domain event.
/// </summary>
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