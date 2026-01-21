namespace Nvx.ConsistentAPI.EventModeling;

public interface IdempotentReadModel
{
  string TableName { get; }

  Task TryProcess(
    FoundEntity foundEntity,
    DatabaseHandlerFactory dbFactory,
    StrongId entityId,
    string? checkpoint,
    ILogger logger,
    CancellationToken cancellationToken);

  bool CanProject(EventModelEvent e);
  bool CanProject(string streamName);
}
