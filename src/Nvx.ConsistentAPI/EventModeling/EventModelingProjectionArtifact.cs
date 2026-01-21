using KurrentDB.Client;

namespace Nvx.ConsistentAPI.EventModeling;

public interface EventModelingProjectionArtifact
{
  /// <summary>
  ///   The projection name, used to define the subscription and to generate the idempotency keys.
  ///   <remarks>MUST BE UNIQUE PER PROJECTION AND SHOULD NEVER CHANGE</remarks>
  /// </summary>
  string Name { get; }

  string SourcePrefix { get; }
  bool CanProject(ResolvedEvent evt);

  Task HandleEvent(
    ResolvedEvent evt,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    Fetcher fetcher,
    KurrentDBClient client);
}
