using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Nvx.ConsistentAPI;

public interface TodoTaskDefinition
{
  public string Type { get; }
  public EventModelingProjectionArtifact Projection { get; }
  public TimeSpan LockLength { get; }
  public Type EntityIdType { get; }
  public Type[] DependingReadModels { get; }

  public Task<Du<EventInsertion, TodoOutcome>> Execute(
    string data,
    Fetcher fetcher,
    StrongId entityId,
    string connectionString,
    ILogger logger);
}

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
    new TodoProjector(SourcePrefix) { Delay = Delay, Expiration = Expiration, Originator = Originator, Type = Type };

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

  private static Guid GetTaskId(Uuid sourceEventUuid, string type) =>
    IdempotentUuid.Generate($"{sourceEventUuid}{type})").ToGuid();

  public override int GetHashCode() => Type.GetHashCode();

  internal class TodoProjector : ProjectionDefinition<SourceEvent, TodoCreated, Entity, ProcessorEntity, StrongGuid>
  {
    private readonly string sourceStreamPrefix;

    internal TodoProjector(string sourceStreamPrefix) => this.sourceStreamPrefix = sourceStreamPrefix;

    public required Func<SourceEvent, Entity, EventMetadata, TodoData> Originator { get; init; }
    public TimeSpan Delay { get; init; } = TimeSpan.Zero;
    public required string Type { get; init; }
    public TimeSpan Expiration { get; init; } = TimeSpan.FromDays(7);

    public override string Name => $"framework-todo-{Type}";

    // ReSharper disable once ConvertToAutoProperty
    public override string SourcePrefix => sourceStreamPrefix;

    public override Option<TodoCreated> Project(
      SourceEvent eventToProject,
      Entity e,
      Option<ProcessorEntity> projectionEntity,
      StrongGuid projectionId,
      Uuid sourceEventUuid,
      EventMetadata metadata)
    {
      var todoData = Originator(eventToProject, e, metadata);
      var startsAt = CalculateStartsAt();
      var expiresAt = startsAt + Expiration;
      return new TodoCreated(
        GetTaskId(sourceEventUuid, Type),
        startsAt,
        expiresAt,
        JsonConvert.SerializeObject(todoData),
        Type,
        eventToProject.GetEntityId().StreamId(),
        JsonConvert.SerializeObject(eventToProject.GetEntityId())
      );

      DateTime CalculateStartsAt() =>
        todoData is OverriddenScheduleTodo overriddenScheduleTodo
          ? overriddenScheduleTodo.ScheduledAt
          : metadata.CreatedAt + Delay;
    }

    public override IEnumerable<StrongGuid> GetProjectionIds(
      SourceEvent sourceEvent,
      Entity sourceEntity,
      Uuid sourceEventId) => [new(GetTaskId(sourceEventId, Type))];
  }
}