using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Framework.Projections.Model;

namespace Nvx.ConsistentAPI.Framework.Projections;

/// <summary>
/// Handles live subscription to the event stream for real-time projection processing.
/// This handler subscribes to new events as they are appended to the store and
/// processes them through all applicable projectors.
/// </summary>
/// <remarks>
/// Unlike the catch-up handler which processes historical events, this handler
/// maintains a live subscription starting from the last checkpoint position.
/// It runs continuously while the Projections feature is enabled.
///
/// The subscription handles different message types:
/// - Event: Process through projectors and emit checkpoint
/// - AllStreamCheckpointReached: Emit snapshot for position tracking
/// - CaughtUp/FellBehind: Status notifications (no action required)
/// </remarks>
/// <param name="projectors">Array of all registered projection artifacts.</param>
/// <param name="fetcher">Fetcher instance for retrieving entities.</param>
/// <param name="emitter">Emitter instance for emitting events.</param>
/// <param name="client">KurrentDB client for subscribing to the event stream.</param>
/// <param name="parser">Function to parse resolved events into domain events.</param>
/// <param name="state">Shared daemon state for tracking progress.</param>
/// <param name="gs">Generator settings containing feature flags.</param>
/// <param name="logger">Logger for diagnostic output.</param>
public class ProjectionSubscriptionHandler(
  EventModelingProjectionArtifact[] projectors,
  Fetcher fetcher,
  Emitter emitter,
  KurrentDBClient client,
  Func<ResolvedEvent, Option<EventModelEvent>> parser,
  ProjectionDaemonState state,
  GeneratorSettings gs,
  ILogger logger)
{
  /// <summary>
  /// Starts the live subscription to the event stream and processes events
  /// through all registered projectors. Runs continuously until the
  /// Projections feature is disabled.
  /// </summary>
  /// <remarks>
  /// The subscription starts from:
  /// - The last checkpoint position if one exists
  /// - The end of the stream if no checkpoint (first startup)
  ///
  /// For each event:
  /// 1. Each projector checks if it can project the event
  /// 2. Applicable projectors process the event
  /// 3. A checkpoint event is emitted after successful projection
  /// 4. State is updated for monitoring
  ///
  /// On subscription errors, the handler waits briefly then reconnects.
  /// Individual projection errors are logged but don't break the subscription.
  /// </remarks>
  public async Task Subscribe()
  {
    while (gs.EnabledFeatures.HasFlag(FrameworkFeatures.Projections))
    {
      try
      {
        var tracker = await ProjectionSetupOperations.GetTracker(fetcher);
        var position = tracker.Checkpoint is null
          ? FromAll.End
          : FromAll.After(new Position(tracker.Checkpoint.Value, tracker.Checkpoint.Value));

        await foreach (var message in client.SubscribeToAll(
                           position,
                           filterOptions: new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents()))
                         .Messages)
        {
          var hasProjected = false;
          switch (message)
          {
            case StreamMessage.Event(var evt):
              foreach (var projector in projectors)
              {
                try
                {
                  if (!projector.CanProject(evt))
                  {
                    continue;
                  }

                  state.IsProjecting = true;
                  await projector.HandleEvent(evt, parser, fetcher, client);
                  state.IsProjecting = false;
                  hasProjected = true;
                }
                catch (Exception ex)
                {
                  state.IsProjecting = false;
                  logger.LogError(
                    ex,
                    "Error during projection daemon subscription for event {Event} with projector {Projector}, won't be retried",
                    evt.Event.EventType,
                    projector.Name);
                }
              }

              if (hasProjected)
              {
                await emitter.Emit(() => new AnyState(
                  new ProjectionCheckpointReached(ProjectionDaemonState.SubscriptionVersion, evt.Event.Position.CommitPosition)));
                state.IncrementProjectedCount();
              }

              state.LastProcessedPosition = evt.Event.Position.CommitPosition;

              break;
            case StreamMessage.AllStreamCheckpointReached(var checkpoint):
              var checkpointTracker = await ProjectionSetupOperations.GetTracker(fetcher);
              await emitter.Emit(() => new AnyState(
                new ProjectionSnapshotReached(
                  ProjectionDaemonState.SubscriptionVersion,
                  checkpointTracker.ExistingProjections,
                  checkpointTracker.UpToDateProjections,
                  checkpoint.CommitPosition)));
              state.LastProcessedPosition = checkpoint.CommitPosition;
              break;
            case StreamMessage.CaughtUp:
              break;
            case StreamMessage.FellBehind:
              break;
          }
        }
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error during projection daemon subscription");
        await Task.Delay(500);
      }
    }
  }
}
