// ReSharper disable ParameterTypeCanBeEnumerable.Local

using EventStore.Client;

namespace Nvx.ConsistentAPI;

public abstract class
  ProjectionDefinition<SourceEvent, ProjectedEvent, SourceEntity, ProjectionEntity,
    ProjectionId> : EventModelingProjectionArtifact
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

  public Task HandleEvent(
    ResolvedEvent evt,
    EventModel.EventParser parser,
    Fetcher fetcher,
    EventStoreClient client)
  {
    return parser(evt)
      .Match(
        async me => await (me switch
        {
          SourceEvent se => Handle(se, evt.Event.EventId, EventMetadata.TryParse(evt)),
          _ => Task.CompletedTask
        }),
        () => Task.CompletedTask);

    async Task Handle(SourceEvent sourceEvent, Uuid sourceEventUuid, EventMetadata metadata)
    {
      await Emit(Decider, client);
      return;

      async Task<Result<(Option<EventModelEvent>, Uuid, EventMetadata)[], ApiError>> Decider()
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
            .Map<(Option<EventModelEvent> evt, Uuid sourceEventUuid, EventMetadata metadata)[]>(t =>
              t.pes.Select(pt => ToProjection(pt, t.e)).ToArray())
            .DefaultValue([]);

        (Option<EventModelEvent>, Uuid sourceEventUuid, EventMetadata) ToProjection(
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

  public bool CanProject(ResolvedEvent e) =>
    e.Event.EventStreamId.StartsWith(SourcePrefix)
    && e.Event.EventType == Naming.ToSpinalCase<SourceEvent>();

  private async Task Emit(
    Func<Task<Result<(Option<EventModelEvent>, Uuid, EventMetadata)[], ApiError>>> decider,
    EventStoreClient esClient)
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

    IEnumerable<EventData> ToEventData(
      EventModelEvent @event,
      Uuid sourceEventUuid,
      int offset,
      EventMetadata? metadata) =>
      IdempotentUuid
        .Generate($"{Name}{sourceEventUuid}{offset}")
        .Apply(id => new[]
        {
          new EventData(
            id,
            @event.EventType,
            @event.ToBytes(),
            (metadata is not null
              ? metadata with { CreatedAt = DateTime.UtcNow, CausationId = sourceEventUuid.ToString() }
              : new EventMetadata(DateTime.UtcNow, Guid.NewGuid().ToString(), sourceEventUuid.ToString(), null, null)
            ).ToBytes()
          )
        });

    async Task<Unit> Go((Option<EventModelEvent>, Uuid, EventMetadata)[] tuples)
    {
      var offset = 0;
      foreach (var tuple in tuples)
      {
        var (evt, sourceEventUuid, metadata) = tuple;
        await evt
          .Match(
            async @event =>
            {
              var result = await esClient.AppendToStreamAsync(
                @event.GetStreamName(),
                StreamState.Any,
                ToEventData(@event, sourceEventUuid, offset++, metadata));

              if (@event is not EventModelSnapshotEvent)
              {
                return unit;
              }

              var currentStreamMetadata =
                await esClient.GetStreamMetadataAsync(@event.GetStreamName()).Map(mdr => mdr.Metadata);

              var newMetadata = new StreamMetadata(
                currentStreamMetadata.MaxCount,
                currentStreamMetadata.MaxAge,
                result.NextExpectedStreamRevision.ToUInt64(),
                currentStreamMetadata.CacheControl,
                currentStreamMetadata.Acl,
                currentStreamMetadata.CustomMetadata);

              await esClient.SetStreamMetadataAsync(@event.GetStreamName(), StreamState.Any, newMetadata);

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
    Uuid sourceEventUuid,
    EventMetadata metadata);

  public abstract IEnumerable<ProjectionId> GetProjectionIds(
    SourceEvent sourceEvent,
    SourceEntity sourceEntity,
    Uuid sourceEventId);

  public override int GetHashCode() => Name.GetHashCode();
}
