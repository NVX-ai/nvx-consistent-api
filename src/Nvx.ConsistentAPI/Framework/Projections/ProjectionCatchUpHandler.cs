using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Framework.Projections.Model;
using Nvx.ConsistentAPI.Metrics;

namespace Nvx.ConsistentAPI.Framework.Projections;

/// <summary>
/// Handles the catch-up process for projections that are behind the current event stream position.
/// This handler reads historical events from the beginning of the stream and processes them
/// through projectors that haven't yet processed those events.
/// </summary>
/// <remarks>
/// The catch-up process is used in two scenarios:
/// 1. When new projections are registered and need to process historical events
/// 2. When a projection is reset via the /rerun-projection endpoint
///
/// Thread-safety is ensured via <see cref="ProjectionDaemonState.CatchUpLock"/> to prevent
/// concurrent catch-up operations.
/// </remarks>
/// <param name="projectors">Array of all registered projection artifacts.</param>
/// <param name="fetcher">Fetcher instance for retrieving entities.</param>
/// <param name="emitter">Emitter instance for emitting events.</param>
/// <param name="client">KurrentDB client for reading the event stream.</param>
/// <param name="parser">Function to parse resolved events into domain events.</param>
/// <param name="state">Shared daemon state for tracking progress.</param>
/// <param name="logger">Logger for diagnostic output.</param>
public class ProjectionCatchUpHandler(
  EventModelingProjectionArtifact[] projectors,
  Fetcher fetcher,
  Emitter emitter,
  KurrentDBClient client,
  Func<ResolvedEvent, Option<EventModelEvent>> parser,
  ProjectionDaemonState state,
  ILogger logger)
{
  /// <summary>
  /// Executes the catch-up process for all projections that are behind.
  /// Reads events from the event store starting from position zero and processes
  /// them through each projector that needs to catch up.
  /// </summary>
  /// <remarks>
  /// The method:
  /// 1. Acquires the catch-up lock to prevent concurrent catch-up operations
  /// 2. Identifies which projections are behind (not in UpToDateProjections)
  /// 3. Reads all events matching the source prefixes of behind projectors
  /// 4. Processes each event through applicable projectors
  /// 5. Marks projections as up-to-date when complete
  /// 6. Updates state for progress monitoring
  ///
  /// Errors during individual event processing are logged but don't stop the catch-up.
  /// Errors at the catch-up level trigger a retry after a delay.
  /// </remarks>
  public async Task CatchUp()
  {
    var keepCatchingUp = true;
    var position = Position.Start;
    while (keepCatchingUp)
    {
      try
      {
        await ProjectionDaemonState.CatchUpLock.WaitAsync();
        var tracker = await ProjectionSetupOperations.GetTracker(fetcher);
        var projectionsBehind = tracker.ExistingProjections.Except(tracker.UpToDateProjections).ToArray();
        state.CatchingUp = projectionsBehind;
        if (projectionsBehind.Length == 0)
        {
          keepCatchingUp = false;
          continue;
        }

        logger.LogInformation(
          "Catching up projections {ProjectionsBehind}, current position {Position}",
          projectionsBehind,
          position);
        var projectorsBehind = projectors.Where(p => projectionsBehind.Contains(p.Name)).ToArray();
        await foreach (var evt in client.ReadAllAsync(
                         Direction.Forwards,
                         position,
                         StreamFilter.Prefix(projectorsBehind.Select(p => p.SourcePrefix).Distinct().ToArray())))
        {
          foreach (var projector in projectorsBehind)
          {
            try
            {
              if (!projector.CanProject(evt))
              {
                continue;
              }

              using var _ = new RunningProjectionCountTracker(projector.Name);
              await projector.HandleEvent(evt, parser, fetcher, client);
              state.IncrementProjectedCount();
            }
            catch (Exception ex)
            {
              logger.LogError(
                ex,
                "Error during catch-up for event {Event} with projector {Projector}, won't be retried",
                evt,
                projector.Name);
            }
          }

          position = evt.Event.Position;
          state.LastCatchUpProcessedPosition = evt.Event.Position.CommitPosition;
        }

        foreach (var projector in projectionsBehind)
        {
          await emitter.Emit(() => new AnyState(new ProjectionUpToDate(ProjectionDaemonState.SubscriptionVersion, projector)));
        }
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error during catch-up for projections");
        await Task.Delay(2_550);
      }
      finally
      {
        ProjectionDaemonState.CatchUpLock.Release();
      }
    }

    state.CatchingUp = [];
    logger.LogInformation("Caught up all projections");
  }
}
