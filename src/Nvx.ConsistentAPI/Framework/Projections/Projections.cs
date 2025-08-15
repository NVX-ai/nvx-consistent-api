// ReSharper disable ParameterTypeCanBeEnumerable.Local

using EventStore.Client;
using Nvx.ConsistentAPI.Store.Events;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI;

public abstract class
  ProjectionDefinition<SourceEvent, ProjectedEvent, SourceEntity, ProjectionEntity, ProjectionId>
  : EventModelingProjectionArtifact
  where SourceEvent : EventModelEvent
  where ProjectedEvent : EventModelEvent
  where SourceEntity : EventModelEntity<SourceEntity>
  where ProjectionEntity : EventModelEntity<ProjectionEntity>
  where ProjectionId : StrongId
{
  /// <summary>
  ///   The projection name, used to define the subscription and to generate the idempotency keys.
  ///   <remarks>MUST BE UNIQUE PER PROJECTION AND SHOULD NEVER CHANGE</remarks>
  /// </summary>
  public abstract string Name { get; }

  public abstract string SourcePrefix { get; }

  public bool CanProject(EventModelEvent e) =>
    e.GetStreamName().StartsWith(SourcePrefix)
    && e.EventType == Naming.ToSpinalCase<SourceEvent>();

  public async Task HandleEvent(
    EventModelEvent me,
    StoredEventMetadata storedMetadata,
    Fetcher fetcher,
    EventStore<EventModelEvent> store)
  {
    await (me switch
    {
      SourceEvent se => Handle(se, storedMetadata.EventId, EventMetadata.From(storedMetadata)),
      _ => Task.CompletedTask
    });
    return;

    async Task Handle(SourceEvent sourceEvent, Guid sourceEventUuid, EventMetadata metadata)
    {
      await Emit(Decider, store);
      return;

      async Task<Result<(Option<EventModelEvent>, Guid, EventMetadata)[], ApiError>> Decider()
      {
        return await
          fetcher
            .Fetch<SourceEntity>(sourceEvent.GetEntityId())
            .Map(r => r.Ent)
            .Async()
            .Map<(SourceEntity e, IEnumerable<ProjectionId> ids)>(e =>
              (e, GetProjectionIds(sourceEvent, e, sourceEventUuid)))
            .Map(async t => await GetEntities(t.ids)
              .Map<
                (ProjectionId id, FetchResult<ProjectionEntity> fr)[],
                (SourceEntity e, (ProjectionId id, Option<ProjectionEntity> pe)[] pes)>(ett =>
                (t.e, ett.Select(et => (et.id, et.fr.Ent)).ToArray())))
            .Map<(Option<EventModelEvent> evt, Guid sourceEventUuid, EventMetadata metadata)[]>(t =>
              t.pes.Select(pt => ToProjection(pt, t.e)).ToArray())
            .DefaultValue([]);

        (Option<EventModelEvent>, Guid sourceEventUuid, EventMetadata) ToProjection(
          (ProjectionId id, Option<ProjectionEntity> pe) pt,
          SourceEntity se) =>
          (Project(sourceEvent, se, pt.pe, pt.id, sourceEventUuid, metadata).Map(e => e as EventModelEvent),
            sourceEventUuid,
            metadata with { CreatedAt = DateTime.UtcNow, CausationId = sourceEventUuid.ToString() });

        async Task<(ProjectionId id, FetchResult<ProjectionEntity> fr)[]> GetEntities(IEnumerable<ProjectionId> ids) =>
          await ids
            .Select<ProjectionId, Func<Task<(ProjectionId, FetchResult<ProjectionEntity>)>>>(id =>
              async () => (id, await fetcher.Fetch<ProjectionEntity>(Some<StrongId>(id))))
            .Parallel();
      }
    }
  }

  private async Task Emit(
    Func<Task<Result<(Option<EventModelEvent>, Guid, EventMetadata)[], ApiError>>> decider,
    EventStore<EventModelEvent> store)
  {
    var i = 0;
    while (i < 1000)
    {
      try
      {
        await decider().Async().Match(async t => await Go(t), _ => Task.FromResult(unit));
        i = 1000;
      }
      catch (WrongExpectedVersionException)
      {
        i++;
        await Task.Delay(i);
      }
    }

    return;

    async Task<Unit> Go((Option<EventModelEvent>, Guid, EventMetadata)[] tuples)
    {
      var offset = 0;
      foreach (var tuple in tuples)
      {
        var (evt, sourceEventUuid, metadata) = tuple;
        await evt
          .Match(
            async @event =>
            {
              var result = await store.Insert(
                new InsertionPayload<EventModelEvent>(
                  @event.GetSwimLane(),
                  @event.GetEntityId(),
                  new AnyStreamState(),
                  [
                    (
                      @event,
                      new EventInsertionMetadataPayload(
                        IdempotentUuid.Generate($"{Name}{sourceEventUuid}{offset++}").ToGuid(),
                        metadata.RelatedUserSub,
                        metadata.CorrelationId ?? Guid.NewGuid().ToString(),
                        sourceEventUuid.ToString(),
                        metadata.CreatedAt)
                    )
                  ]));

              if (@event is not EventModelSnapshotEvent)
              {
                return unit;
              }

              foreach (var insertionSuccess in result.Option)
              {
                await store.TruncateStream(
                  @event.GetSwimLane(),
                  @event.GetEntityId(),
                  insertionSuccess.StreamPosition);
              }

              return unit;
            },
            () => unit.ToTask()
          );
      }

      return unit;
    }
  }

  public abstract Option<ProjectedEvent> Project(
    SourceEvent eventToProject,
    SourceEntity e,
    Option<ProjectionEntity> projectionEntity,
    ProjectionId projectionId,
    Guid sourceEventUuid,
    EventMetadata metadata);

  public abstract IEnumerable<ProjectionId> GetProjectionIds(
    SourceEvent sourceEvent,
    SourceEntity sourceEntity,
    Guid sourceEventId);

  public override int GetHashCode() => Name.GetHashCode();
}
