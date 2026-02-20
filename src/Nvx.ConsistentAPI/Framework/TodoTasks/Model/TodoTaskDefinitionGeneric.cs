using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Generic implementation of <see cref="TodoTaskDefinition"/> that binds a todo task to specific
/// data, entity, event, and ID types. Provides deserialization of the todo payload, entity fetching,
/// and delegation to the configured <see cref="Action"/> for execution.
/// </summary>
public class TodoTaskDefinition<DataShape, Entity, SourceEvent, EntityId> : TodoTaskDefinition
  where DataShape : TodoData
  where Entity : EventModelEntity<Entity>
  where SourceEvent : EventModelEvent
  where EntityId : StrongId
{
  public required
    Func<DataShape, Entity, Fetcher, DatabaseHandlerFactory, ILogger, Task<Du<EventInsertion, TodoOutcome>>> Action
  {
    get;
    init;
  }

  public required Func<SourceEvent, Entity, EventMetadata, TodoData> Originator { get; init; }
  public TimeSpan Delay { get; init; } = TimeSpan.Zero;
  public TimeSpan Expiration { get; init; } = TimeSpan.FromDays(7);
  public required string SourcePrefix { get; init; }
  public Type EntityIdType { get; } = typeof(EntityId);
  public Type[] DependingReadModels { get; init; } = [];
  public required string Type { get; init; }
  public TimeSpan LockLength { get; init; } = TimeSpan.FromHours(1);

  public async Task<Du<EventInsertion, TodoOutcome>> Execute(
    string data,
    Fetcher fetcher,
    StrongId entityId,
    string connectionString,
    ILogger logger)
  {
    try
    {
      return await DeserializeData(data, logger)
        .Async()
        .Bind(d => fetcher
          .Fetch<Entity>(entityId)
          .Map(fr => fr.Ent.Map(e => (e, fr.Revision)))
          .Async()
          .Map(e => (d, e.e, e.Revision)))
        .Map(async t =>
          await Action(
              t.d,
              t.e,
              fetcher,
              new DatabaseHandlerFactory(connectionString, logger),
              logger)
            .Map(du =>
              du.Match(
                ei => ei.WithRevision(t.Revision).Apply(First<EventInsertion, TodoOutcome>),
                Second<EventInsertion, TodoOutcome>))
        )
        .DefaultValue(TodoOutcome.Retry);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed executing todo:\n{ArgItem1}\nFor task {ArgItem2}", data, Type);
      return TodoOutcome.Retry;
    }
  }

  public EventModelingProjectionArtifact Projection =>
    new TodoProjector<SourceEvent, Entity>(SourcePrefix) { Delay = Delay, Expiration = Expiration, Originator = Originator, Type = Type };

  private Option<DataShape> DeserializeData(string data, ILogger logger)
  {
    try
    {
      return JsonConvert.DeserializeObject<DataShape>(data).Apply(Optional);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed deserializing todo data for task {TaskType}", Type);
      return None;
    }
  }

  public override int GetHashCode() => Type.GetHashCode();
}
