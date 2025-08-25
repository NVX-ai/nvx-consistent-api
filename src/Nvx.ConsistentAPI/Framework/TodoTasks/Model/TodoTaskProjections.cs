using EventStore.Client;

namespace Nvx.ConsistentAPI;

public class TodoProcessorCompletedToSnapshot
  : ProjectionDefinition<TodoCompleted, TodoModelSnapshot, ProcessorEntity, ProcessorEntity, StrongGuid>
{
  public override string Name => "framework-todo-processor-snapshot-completed-tasks";
  public override string SourcePrefix => ProcessorEntity.StreamPrefix;

  public override Option<TodoModelSnapshot> Project(
    TodoCompleted eventToProject,
    ProcessorEntity e,
    Option<ProcessorEntity> projectionEntity,
    StrongGuid projectionId,
    Guid sourceEventUuid,
    EventMetadata metadata) =>
    new TodoModelSnapshot(
      eventToProject.Id,
      e.StartsAt,
      e.ExpiresAt,
      e.LockedUntil,
      eventToProject.CompletedAt,
      e.RelatedEntityId,
      e.JsonData,
      e.Type,
      e.SerializedRelatedEntityId,
      e.EventPosition is { } pos ? new Position(pos, pos) : null);

  public override IEnumerable<StrongGuid> GetProjectionIds(
    TodoCompleted sourceEvent,
    ProcessorEntity sourceEntity,
    Guid sourceEventId) => [new(sourceEvent.Id)];
}
