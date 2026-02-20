namespace Nvx.ConsistentAPI;

/// <summary>
/// Event emitted when a lock is requested on a todo task before execution begins.
/// </summary>
public record TodoLockRequested(Guid Id, DateTime RequestedAt, TimeSpan Length) : EventModelEvent
{
  public string GetStreamName() => ProcessorEntity.GetStreamName(Id);

  public StrongId GetEntityId() => new StrongGuid(Id);
}