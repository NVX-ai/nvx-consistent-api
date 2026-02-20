namespace Nvx.ConsistentAPI;

/// <summary>
/// Event emitted when a todo task has been successfully completed.
/// </summary>
public record TodoCompleted(Guid Id, DateTime CompletedAt) : EventModelEvent
{
  public string GetStreamName() => ProcessorEntity.GetStreamName(Id);

  public StrongId GetEntityId() => new StrongGuid(Id);
}
