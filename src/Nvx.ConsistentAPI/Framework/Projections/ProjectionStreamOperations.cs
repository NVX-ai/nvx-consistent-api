using KurrentDB.Client;

namespace Nvx.ConsistentAPI.Framework.Projections;

/// <summary>
/// Provides operations for appending projection events to the event store.
/// Handles stream operations including event appending, snapshot truncation,
/// and retry logic for optimistic concurrency conflicts.
/// </summary>
/// <remarks>
/// This class encapsulates the complexity of:
/// - Building and appending projection events
/// - Managing stream metadata for snapshot events (truncation)
/// - Retry logic with exponential backoff for concurrency conflicts
/// </remarks>
public static class ProjectionStreamOperations
{
  /// <summary>
  /// Appends a projected event to its target stream in the event store.
  /// For snapshot events, also updates stream metadata to enable truncation.
  /// </summary>
  /// <param name="client">KurrentDB client for stream operations.</param>
  /// <param name="projectionName">Name of the projection for idempotent UUID generation.</param>
  /// <param name="event">The domain event to append.</param>
  /// <param name="sourceEventUuid">UUID of the source event for causation tracking.</param>
  /// <param name="offset">Offset for multiple events from a single projection.</param>
  /// <param name="metadata">Optional metadata to attach to the event.</param>
  /// <remarks>
  /// When the event is a snapshot event (<see cref="EventModelSnapshotEvent"/>),
  /// the stream's TruncateBefore metadata is updated to enable cleanup of
  /// older events that are superseded by the snapshot.
  /// </remarks>
  public static async Task AppendEventToStream(
    KurrentDBClient client,
    string projectionName,
    EventModelEvent @event,
    Uuid sourceEventUuid,
    int offset,
    EventMetadata? metadata)
  {
    var eventData = ProjectionEventDataBuilder.Build(projectionName, @event, sourceEventUuid, offset, metadata);
    var result = await client.AppendToStreamAsync(
      @event.GetStreamName(),
      StreamState.Any,
      eventData);

    if (@event is EventModelSnapshotEvent
        && result.NextExpectedStreamState.HasPosition
        && result.NextExpectedStreamState.ToInt64() >= 0)
    {
      await UpdateStreamTruncateBefore(client, @event.GetStreamName(), result.NextExpectedStreamState.ToInt64());
    }
  }

  /// <summary>
  /// Updates stream metadata to set the TruncateBefore position.
  /// This enables the event store to clean up events before the snapshot.
  /// </summary>
  /// <param name="client">KurrentDB client for metadata operations.</param>
  /// <param name="streamName">Name of the stream to update.</param>
  /// <param name="truncateBefore">Position before which events can be truncated.</param>
  private static async Task UpdateStreamTruncateBefore(
    KurrentDBClient client,
    string streamName,
    long truncateBefore)
  {
    var currentStreamMetadata = await client.GetStreamMetadataAsync(streamName).Map(mdr => mdr.Metadata);

    var newMetadata = new StreamMetadata(
      currentStreamMetadata.MaxCount,
      currentStreamMetadata.MaxAge,
      Convert.ToUInt64(truncateBefore),
      currentStreamMetadata.CacheControl,
      currentStreamMetadata.Acl,
      currentStreamMetadata.CustomMetadata);

    await client.SetStreamMetadataAsync(streamName, StreamState.Any, newMetadata);
  }

  /// <summary>
  /// Processes an array of projection tuples, appending each event to the store.
  /// Handles optional events (None values are skipped).
  /// </summary>
  /// <param name="client">KurrentDB client for stream operations.</param>
  /// <param name="projectionName">Name of the projection for idempotent UUID generation.</param>
  /// <param name="tuples">
  /// Array of tuples containing:
  /// - Optional event (None if projection decided not to emit)
  /// - Source event UUID for causation tracking
  /// - Metadata for the projected event
  /// </param>
  /// <returns>Unit value indicating completion.</returns>
  public static async Task<Unit> ProcessProjectionTuples(
    KurrentDBClient client,
    string projectionName,
    (Option<EventModelEvent>, Uuid, EventMetadata)[] tuples)
  {
    var offset = 0;
    foreach (var (evt, sourceEventUuid, metadata) in tuples)
    {
      await evt.Match(
        async @event =>
        {
          await AppendEventToStream(client, projectionName, @event, sourceEventUuid, offset++, metadata);
          return unit;
        },
        () => unit.ToTask());
    }

    return unit;
  }

  /// <summary>
  /// Executes a projection decision function with retry logic for optimistic concurrency conflicts.
  /// Retries up to 1000 times with progressive delay when WrongExpectedVersionException occurs.
  /// </summary>
  /// <param name="decider">
  /// Function that computes which events to emit. Called on each retry attempt
  /// to get fresh entity state.
  /// </param>
  /// <param name="client">KurrentDB client for stream operations.</param>
  /// <param name="projectionName">Name of the projection for idempotent UUID generation.</param>
  /// <remarks>
  /// The retry mechanism handles concurrent writes to the same stream:
  /// - On WrongExpectedVersionException, waits (attempt count) milliseconds
  /// - Calls decider again to recompute based on latest state
  /// - Continues until success or max attempts reached
  ///
  /// This is particularly important for projections that may be triggered
  /// by multiple concurrent events targeting the same projected entity.
  /// </remarks>
  public static async Task EmitWithRetry(
    Func<Task<Result<(Option<EventModelEvent>, Uuid, EventMetadata)[], ApiError>>> decider,
    KurrentDBClient client,
    string projectionName)
  {
    var i = 0;
    while (i < 1000)
    {
      try
      {
        await decider().Async().Match(
          async t => await ProcessProjectionTuples(client, projectionName, t),
          _ => Task.FromResult(unit));
        i = 1000;
      }
      catch (WrongExpectedVersionException)
      {
        i++;
        await Task.Delay(i);
      }
    }
  }
}
