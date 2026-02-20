namespace Nvx.ConsistentAPI;

/// <summary>
/// Event emitted when a lock on a todo task is released, allowing it to be retried.
/// </summary>
public record TodoLockReleased(Guid Id) : EventModelEvent
{
  public string GetStreamName() => ProcessorEntity.GetStreamName(Id);

  public StrongId GetEntityId() => new StrongGuid(Id);
}