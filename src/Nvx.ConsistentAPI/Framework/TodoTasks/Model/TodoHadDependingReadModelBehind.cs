namespace Nvx.ConsistentAPI;

/// <summary>
/// Event emitted when a todo task's execution is deferred because its dependent read models
/// are not yet up-to-date with the event stream. Causes an incremental backoff lock.
/// </summary>
public record TodoHadDependingReadModelBehind(Guid Id) : EventModelEvent
{
  public string GetStreamName() => ProcessorEntity.GetStreamName(Id);

  public StrongId GetEntityId() => new StrongGuid(Id);
}
