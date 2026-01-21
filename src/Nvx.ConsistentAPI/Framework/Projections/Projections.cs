// ReSharper disable ParameterTypeCanBeEnumerable.Local

using KurrentDB.Client;
using Nvx.ConsistentAPI.Framework.Projections;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Abstract base class for defining event projections in the Event Modeling framework.
/// Projections transform events from one entity type to another, enabling derived/computed
/// views of data based on source events.
/// </summary>
/// <remarks>
/// A projection listens for specific source events and produces projected events that are
/// written to target entity streams. This enables:
/// - Denormalization of data for read optimization
/// - Computing derived state from multiple source events
/// - Creating different views of the same underlying data
///
/// Projections are idempotent - rerunning a projection produces the same results due to
/// deterministic UUID generation based on the source event.
///
/// To implement a projection:
/// 1. Inherit from this class with appropriate type parameters
/// 2. Implement <see cref="Name"/> with a unique, immutable identifier
/// 3. Implement <see cref="SourcePrefix"/> to filter source streams
/// 4. Implement <see cref="GetProjectionIds"/> to determine target entities
/// 5. Implement <see cref="Project"/> to transform source events to projected events
/// </remarks>
/// <typeparam name="SourceEvent">The type of event that triggers this projection.</typeparam>
/// <typeparam name="ProjectedEvent">The type of event that this projection produces.</typeparam>
/// <typeparam name="SourceEntity">The entity type associated with the source event.</typeparam>
/// <typeparam name="ProjectionEntity">The entity type that receives projected events.</typeparam>
/// <typeparam name="ProjectionId">The strongly-typed ID for the projection target entity.</typeparam>
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
  /// The unique name identifying this projection. Used for subscription management
  /// and idempotent UUID generation.
  /// </summary>
  /// <remarks>
  /// MUST BE UNIQUE PER PROJECTION AND SHOULD NEVER CHANGE.
  /// Changing this name will cause the projection to be treated as a new projection,
  /// potentially resulting in duplicate projected events.
  /// </remarks>
  public abstract string Name { get; }

  /// <summary>
  /// The stream name prefix used to filter which events this projection receives.
  /// Only events from streams starting with this prefix will be considered.
  /// </summary>
  public abstract string SourcePrefix { get; }

  /// <summary>
  /// Handles an incoming event from the event store subscription.
  /// Parses the event and delegates to the projection logic if it matches the source event type.
  /// </summary>
  /// <param name="evt">The resolved event from the event store.</param>
  /// <param name="parser">Function to parse the raw event into a domain event.</param>
  /// <param name="fetcher">Fetcher for retrieving entity state.</param>
  /// <param name="client">KurrentDB client for emitting projected events.</param>
  /// <returns>Task that completes when the event has been processed.</returns>
  public Task HandleEvent(
    ResolvedEvent evt,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    Fetcher fetcher,
    KurrentDBClient client) =>
    parser(evt).Match(
      async me => await (me switch
      {
        SourceEvent se => HandleSourceEvent(se, evt.Event.EventId, EventMetadata.TryParse(evt), fetcher, client),
        _ => Task.CompletedTask
      }),
      () => Task.CompletedTask);

  /// <summary>
  /// Determines whether this projection can process the given event.
  /// Checks both stream prefix and event type.
  /// </summary>
  /// <param name="e">The resolved event to check.</param>
  /// <returns>True if this projection should process the event; otherwise false.</returns>
  public bool CanProject(ResolvedEvent e) =>
    e.Event.EventStreamId.StartsWith(SourcePrefix)
    && e.Event.EventType == Naming.ToSpinalCase<SourceEvent>();

  /// <summary>
  /// Returns a hash code based on the projection name.
  /// </summary>
  public override int GetHashCode() => Name.GetHashCode();

  /// <summary>
  /// Core projection logic that transforms a source event into a projected event.
  /// Implement this method to define how source events are projected.
  /// </summary>
  /// <param name="eventToProject">The source event to project.</param>
  /// <param name="e">The current state of the source entity.</param>
  /// <param name="projectionEntity">
  /// The current state of the projection target entity, if it exists.
  /// Use this to implement update-or-create logic.
  /// </param>
  /// <param name="projectionId">The ID of the target projection entity.</param>
  /// <param name="sourceEventUuid">UUID of the source event for causation tracking.</param>
  /// <param name="metadata">Metadata from the source event.</param>
  /// <returns>
  /// Some(event) to emit a projected event, or None to skip projection for this target.
  /// </returns>
  public abstract Option<ProjectedEvent> Project(
    SourceEvent eventToProject,
    SourceEntity e,
    Option<ProjectionEntity> projectionEntity,
    ProjectionId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata);

  /// <summary>
  /// Determines which projection entities should receive projected events for a source event.
  /// This enables one-to-many projections where a single source event can affect multiple targets.
  /// </summary>
  /// <param name="sourceEvent">The source event being projected.</param>
  /// <param name="sourceEntity">The current state of the source entity.</param>
  /// <param name="sourceEventId">UUID of the source event.</param>
  /// <returns>
  /// Enumerable of projection target IDs. The projection will be executed once for each ID.
  /// </returns>
  public abstract IEnumerable<ProjectionId> GetProjectionIds(
    SourceEvent sourceEvent,
    SourceEntity sourceEntity,
    Uuid sourceEventId);

  /// <summary>
  /// Internal handler for source events. Orchestrates the projection process
  /// with retry logic for concurrency conflicts.
  /// </summary>
  private async Task HandleSourceEvent(
    SourceEvent sourceEvent,
    Uuid sourceEventUuid,
    EventMetadata metadata,
    Fetcher fetcher,
    KurrentDBClient client)
  {
    await ProjectionStreamOperations.EmitWithRetry(
      () => BuildProjectionDecisions(sourceEvent, sourceEventUuid, metadata, fetcher),
      client,
      Name);
  }

  /// <summary>
  /// Builds the projection decisions by fetching all required entities and
  /// computing the projected events for each target.
  /// </summary>
  /// <param name="sourceEvent">The source event to project.</param>
  /// <param name="sourceEventUuid">UUID of the source event.</param>
  /// <param name="metadata">Metadata from the source event.</param>
  /// <param name="fetcher">Fetcher for retrieving entity state.</param>
  /// <returns>
  /// Array of tuples containing the optional projected event, source UUID, and metadata
  /// for each projection target.
  /// </returns>
  private async Task<Result<(Option<EventModelEvent>, Uuid, EventMetadata)[], ApiError>> BuildProjectionDecisions(
    SourceEvent sourceEvent,
    Uuid sourceEventUuid,
    EventMetadata metadata,
    Fetcher fetcher)
  {
    return await fetcher
      .Fetch<SourceEntity>(sourceEvent.GetEntityId())
      .Map(r => r.Ent)
      .Async()
      .Map<(SourceEntity e, IEnumerable<ProjectionId> ids)>(e =>
        (e, GetProjectionIds(sourceEvent, e, sourceEventUuid)))
      .Map(async t => await FetchProjectionEntities(t.ids, fetcher)
        .Map<
          (ProjectionId id, FetchResult<ProjectionEntity> fr)[],
          (SourceEntity e, (ProjectionId id, Option<ProjectionEntity> pe)[] pes)>(ett =>
          (t.e, ett.Select(et => (et.id, et.fr.Ent)).ToArray())))
      .Map<(Option<EventModelEvent> evt, Uuid sourceEventUuid, EventMetadata metadata)[]>(t =>
        t.pes.Select(pt => BuildProjectionTuple(pt, t.e, sourceEvent, sourceEventUuid, metadata)).ToArray())
      .DefaultValue([]);
  }

  /// <summary>
  /// Builds a single projection tuple by invoking the Project method and
  /// assembling the result with metadata.
  /// </summary>
  private (Option<EventModelEvent>, Uuid, EventMetadata) BuildProjectionTuple(
    (ProjectionId id, Option<ProjectionEntity> pe) pt,
    SourceEntity sourceEntity,
    SourceEvent sourceEvent,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    (Project(sourceEvent, sourceEntity, pt.pe, pt.id, sourceEventUuid, metadata).Map(e => e as EventModelEvent),
      sourceEventUuid,
      metadata with { CreatedAt = DateTime.UtcNow, CausationId = sourceEventUuid.ToString() });

  /// <summary>
  /// Fetches all projection target entities in parallel for efficient processing.
  /// </summary>
  /// <param name="ids">The IDs of projection entities to fetch.</param>
  /// <param name="fetcher">Fetcher for retrieving entity state.</param>
  /// <returns>Array of tuples pairing each ID with its fetch result.</returns>
  private static async Task<(ProjectionId id, FetchResult<ProjectionEntity> fr)[]> FetchProjectionEntities(
    IEnumerable<ProjectionId> ids,
    Fetcher fetcher) =>
    await ids
      .Select<ProjectionId, Func<Task<(ProjectionId, FetchResult<ProjectionEntity>)>>>(id =>
        async () => (id, await fetcher.Fetch<ProjectionEntity>(Some<StrongId>(id))))
      .Parallel();
}
