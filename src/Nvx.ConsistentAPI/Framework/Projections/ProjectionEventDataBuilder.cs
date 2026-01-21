using KurrentDB.Client;

namespace Nvx.ConsistentAPI.Framework.Projections;

/// <summary>
/// Builds EventData objects for projection events with idempotent UUIDs.
/// This ensures that projection events can be safely re-emitted without
/// creating duplicates in the event store.
/// </summary>
/// <remarks>
/// Idempotency is achieved by generating deterministic UUIDs based on:
/// - The projection name
/// - The source event's UUID
/// - An offset for multiple events from a single projection
///
/// This allows projections to be rerun safely - duplicate events will be
/// rejected by the event store due to matching event IDs.
/// </remarks>
public static class ProjectionEventDataBuilder
{
  /// <summary>
  /// Builds an EventData array for appending a projected event to the event store.
  /// </summary>
  /// <param name="projectionName">
  /// The unique name of the projection, used in idempotent UUID generation.
  /// </param>
  /// <param name="event">The domain event to be projected.</param>
  /// <param name="sourceEventUuid">
  /// The UUID of the source event that triggered this projection.
  /// Used for causation tracking and idempotent UUID generation.
  /// </param>
  /// <param name="offset">
  /// Offset index when a single source event produces multiple projected events.
  /// Ensures unique idempotent UUIDs for each projected event.
  /// </param>
  /// <param name="metadata">
  /// Optional metadata from the source event. If provided, it's updated with
  /// current timestamp and causation ID. If null, new metadata is created.
  /// </param>
  /// <returns>
  /// An enumerable containing a single EventData ready for appending to the store.
  /// </returns>
  public static IEnumerable<EventData> Build(
    string projectionName,
    EventModelEvent @event,
    Uuid sourceEventUuid,
    int offset,
    EventMetadata? metadata) =>
    IdempotentUuid
      .Generate($"{projectionName}{sourceEventUuid}{offset}")
      .Apply(id => new[]
      {
        new EventData(
          id,
          @event.EventType,
          @event.ToBytes(),
          BuildMetadata(sourceEventUuid, metadata).ToBytes()
        )
      });

  /// <summary>
  /// Builds or updates event metadata with the current timestamp and causation ID.
  /// </summary>
  /// <param name="sourceEventUuid">The UUID of the source event for causation tracking.</param>
  /// <param name="metadata">Existing metadata to update, or null to create new metadata.</param>
  /// <returns>EventMetadata with updated timestamp and causation information.</returns>
  private static EventMetadata BuildMetadata(Uuid sourceEventUuid, EventMetadata? metadata) =>
    metadata is not null
      ? metadata with { CreatedAt = DateTime.UtcNow, CausationId = sourceEventUuid.ToString() }
      : new EventMetadata(DateTime.UtcNow, Guid.NewGuid().ToString(), sourceEventUuid.ToString(), null, null);
}
