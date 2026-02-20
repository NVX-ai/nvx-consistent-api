using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Defines the contract for a todo task type, including its execution logic, lock duration,
/// entity ID type, and dependent read models that must be up-to-date before processing.
/// </summary>
public interface TodoTaskDefinition
{
  string Type { get; }
  EventModelingProjectionArtifact Projection { get; }
  TimeSpan LockLength { get; }
  Type EntityIdType { get; }
  Type[] DependingReadModels { get; }

  Task<Du<EventInsertion, TodoOutcome>> Execute(
    string data,
    Fetcher fetcher,
    StrongId entityId,
    string connectionString,
    ILogger logger);
}
