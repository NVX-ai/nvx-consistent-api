using Nvx.ConsistentAPI.Framework.Projections.Model;

namespace Nvx.ConsistentAPI.Framework.Projections;

/// <summary>
/// Provides static helper methods for projection daemon initialization and setup.
/// Handles fetching projection tracker state and registering new projections
/// during daemon startup.
/// </summary>
/// <remarks>
/// These operations are performed once during daemon initialization to:
/// 1. Retrieve or create the projection tracker entity
/// 2. Emit initial snapshot events for first-time setup
/// 3. Register any newly added projections that don't exist in the tracker
/// </remarks>
public static class ProjectionSetupOperations
{
  /// <summary>
  /// Fetches the current projection tracker entity from the event store.
  /// The tracker maintains the list of registered projections, their up-to-date
  /// status, and the last checkpoint position.
  /// </summary>
  /// <param name="fetcher">The fetcher instance for retrieving entities.</param>
  /// <returns>
  /// The existing tracker entity if found, or a default empty tracker
  /// if no tracker exists yet (first-time startup).
  /// </returns>
  public static Task<ProjectionTrackerEntity> GetTracker(Fetcher fetcher) => fetcher
    .Fetch<ProjectionTrackerEntity>(new ProjectionTrackerId(ProjectionDaemonState.SubscriptionVersion))
    .Map(fr => fr.Ent)
    .Async()
    .DefaultValue(ProjectionTrackerEntity.Defaulted(new ProjectionTrackerId(ProjectionDaemonState.SubscriptionVersion)));

  /// <summary>
  /// Performs initial projection setup by emitting a snapshot event.
  /// On first startup (no existing tracker), marks all projections as up-to-date.
  /// On subsequent startups, preserves the existing tracker state.
  /// </summary>
  /// <param name="fetcher">The fetcher instance for retrieving entities.</param>
  /// <param name="emitter">The emitter instance for emitting events.</param>
  /// <param name="projectionNames">Array of all projection names defined in the system.</param>
  /// <remarks>
  /// First-time setup assumes all projections start from the current position,
  /// so they're marked as up-to-date with no checkpoint (will subscribe from end).
  /// </remarks>
  public static async Task FirstSetup(Fetcher fetcher, Emitter emitter, string[] projectionNames)
  {
    var tracker = await GetTracker(fetcher);

    var evt = tracker.Checkpoint is null && tracker.ExistingProjections.Length == 0
      ? new ProjectionSnapshotReached(
        ProjectionDaemonState.SubscriptionVersion,
        projectionNames,
        projectionNames,
        null)
      : new ProjectionSnapshotReached(
        tracker.Version,
        tracker.ExistingProjections,
        tracker.UpToDateProjections,
        tracker.Checkpoint);

    await emitter.Emit(() => new AnyState(evt));
  }

  /// <summary>
  /// Registers any new projections that have been added to the codebase but
  /// don't yet exist in the projection tracker. New projections are registered
  /// as not up-to-date, triggering catch-up processing on startup.
  /// </summary>
  /// <param name="fetcher">The fetcher instance for retrieving entities.</param>
  /// <param name="emitter">The emitter instance for emitting events.</param>
  /// <param name="projectionNames">Array of all projection names defined in the system.</param>
  /// <remarks>
  /// This enables adding new projections to a running system - they will
  /// automatically catch up from the beginning of the event stream.
  /// </remarks>
  public static async Task RegisterNewProjections(Fetcher fetcher, Emitter emitter, string[] projectionNames)
  {
    var tracker = await GetTracker(fetcher);
    foreach (var missingProjection in projectionNames.Except(tracker.ExistingProjections).ToArray())
    {
      await emitter.Emit(() => new AnyState(new ProjectionRegistered(ProjectionDaemonState.SubscriptionVersion, missingProjection)));
    }
  }
}
