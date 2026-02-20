using KurrentDB.Client;
using Newtonsoft.Json;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Projection that creates <see cref="TodoCreated"/> events from source domain events,
/// enabling todo tasks to be triggered automatically by domain activity.
/// </summary>
internal class TodoProjector<SourceEvent, Entity>
  : ProjectionDefinition<SourceEvent, TodoCreated, Entity, ProcessorEntity, StrongGuid>
  where SourceEvent : EventModelEvent
  where Entity : EventModelEntity<Entity>
{
  private readonly string sourceStreamPrefix;

  internal TodoProjector(string sourceStreamPrefix)
  {
    this.sourceStreamPrefix = sourceStreamPrefix;
  }

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

  private static Guid GetTaskId(Uuid sourceEventUuid, string type) =>
    IdempotentUuid.Generate($"{sourceEventUuid}{type})").ToGuid();
}
