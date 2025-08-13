using EventStore.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.Framework.Projections.Model;
using Nvx.ConsistentAPI.InternalTooling;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI.Framework.Projections;

public class ProjectionDaemon(
  EventModelingProjectionArtifact[] projectors,
  Fetcher fetcher,
  Emitter emitter,
  EventStoreClient client,
  EventStore<EventModelEvent> store,
  Func<ResolvedEvent, Option<EventModelEvent>> parser,
  WebApplication app,
  GeneratorSettings gs,
  ILogger logger)
{
  private const string SubscriptionVersion = "1";
  private static readonly SemaphoreSlim CatchUpLock = new(1);
  private string[] catchingUp = [];
  private ulong lastCatchUpProcessedPosition;
  private ulong lastProcessedPosition;
  private int projectedCount;
  private bool isProjecting;

  public ProjectorDaemonInsights Insights(ulong lastEventPosition)
  {
    var daemonPercentage = lastEventPosition == 0
      ? 100m
      : Convert.ToDecimal(lastProcessedPosition) * 100m / Convert.ToDecimal(lastEventPosition);
    var catchUpPercentage = lastEventPosition == 0 || catchingUp.Length == 0
      ? 100m
      : Convert.ToDecimal(lastCatchUpProcessedPosition) * 100m / Convert.ToDecimal(lastEventPosition);

    return new ProjectorDaemonInsights(
      lastProcessedPosition,
      Math.Min(100m, daemonPercentage),
      catchingUp,
      lastCatchUpProcessedPosition,
      Math.Min(100m, catchUpPercentage),
      projectedCount,
      isProjecting);
  }

  public async Task Initialize()
  {
    if (!gs.EnabledFeatures.HasFlag(FrameworkFeatures.Projections))
    {
      return;
    }

    var projectionNames = projectors.Select(p => p.Name).ToArray();
    await FirstSetup();
    await RegisterNewProjections();
    _ = Subscribe();
    _ = CatchUp();

    Delegate resetDelegate = async (HttpContext context) =>
    {
      await FrameworkSecurity
        .Authorization(context, fetcher, emitter, gs, new PermissionsRequireOne("admin"), None)
        .Iter(
          async _ =>
          {
            if (context.Request.RouteValues["projectionName"] is string projectionName
                && projectionNames.Contains(projectionName))
            {
              await ResetProjection(projectionName);
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new { });
          },
          async e => await e.Respond(context));
    };

    app
      .MapGet("/rerun-projection/{projectionName}", resetDelegate)
      .WithName("rerun-projection")
      .WithDescription(
        "Reruns a projection, projections are idempotent, so previously generated events will be ignored."
      )
      .WithTags(OperationTags.FrameworkManagement)
      .WithOpenApi(o =>
      {
        o.OperationId = "rerunProjection";
        o.Parameters.Add(
          new OpenApiParameter
          {
            Name = "projectionName",
            In = ParameterLocation.Path,
            Required = true,
            Schema = new OpenApiSchema
            {
              Type = "string",
              Enum = projectionNames.Select(IOpenApiAny (p) => new OpenApiString(p)).ToList()
            }
          });
        return o;
      })
      .ApplyAuth(new PermissionsRequireOne("admin"));

    return;

    async Task ResetProjection(string projectionName)
    {
      await emitter.Emit(() => new AnyState(new ProjectionReset(SubscriptionVersion, projectionName)));
      logger.LogInformation("Resetting projection {ProjectionName}", projectionName);
      _ = CatchUp();
    }

    async Task FirstSetup()
    {
      var tracker = await GetTracker();

      var evt = tracker.Checkpoint is null && tracker.ExistingProjections.Length == 0
        ? new ProjectionSnapshotReached(SubscriptionVersion, projectionNames, projectionNames, null)
        : new ProjectionSnapshotReached(
          tracker.Version,
          tracker.ExistingProjections,
          tracker.UpToDateProjections,
          tracker.Checkpoint);

      await emitter.Emit(() => new AnyState(evt));
    }

    async Task RegisterNewProjections()
    {
      var tracker = await GetTracker();
      foreach (var missingProjection in projectionNames.Except(tracker.ExistingProjections).ToArray())
      {
        await emitter.Emit(() => new AnyState(new ProjectionRegistered(SubscriptionVersion, missingProjection)));
      }
    }

    Task<ProjectionTrackerEntity> GetTracker() => fetcher
      .Fetch<ProjectionTrackerEntity>(new ProjectionTrackerId(SubscriptionVersion))
      .Map(fr => fr.Ent)
      .Async()
      .DefaultValue(ProjectionTrackerEntity.Defaulted(new ProjectionTrackerId(SubscriptionVersion)));

    async Task CatchUp()
    {
      var keepCatchingUp = true;
      var position = Position.Start;
      while (keepCatchingUp)
      {
        try
        {
          await CatchUpLock.WaitAsync();
          var tracker = await GetTracker();
          var projectionsBehind = tracker.ExistingProjections.Except(tracker.UpToDateProjections).ToArray();
          catchingUp = projectionsBehind;
          if (projectionsBehind.Length == 0)
          {
            keepCatchingUp = false;
            continue;
          }

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
                await projector.HandleEvent(evt, parser, fetcher, store);
                Interlocked.Increment(ref projectedCount);
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
            lastCatchUpProcessedPosition = evt.Event.Position.CommitPosition;
          }

          foreach (var projector in projectionsBehind)
          {
            await emitter.Emit(() => new AnyState(new ProjectionUpToDate(SubscriptionVersion, projector)));
          }
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Error during catch-up for projections");
          await Task.Delay(2_550);
        }
        finally
        {
          CatchUpLock.Release();
        }
      }

      catchingUp = [];
    }

    async Task Subscribe()
    {
      while (gs.EnabledFeatures.HasFlag(FrameworkFeatures.Projections))
      {
        try
        {
          var tracker = await GetTracker();
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

                    isProjecting = true;
                    await projector.HandleEvent(evt, parser, fetcher, store);
                    isProjecting = false;
                    hasProjected = true;
                  }
                  catch (Exception ex)
                  {
                    isProjecting = false;
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
                    new ProjectionCheckpointReached(SubscriptionVersion, evt.Event.Position.CommitPosition)));
                  Interlocked.Increment(ref projectedCount);
                }

                lastProcessedPosition = evt.Event.Position.CommitPosition;

                break;
              case StreamMessage.AllStreamCheckpointReached(var checkpoint):
                var checkpointTracker = await GetTracker();
                await emitter.Emit(() => new AnyState(
                  new ProjectionSnapshotReached(
                    SubscriptionVersion,
                    checkpointTracker.ExistingProjections,
                    checkpointTracker.UpToDateProjections,
                    checkpoint.CommitPosition)));
                lastProcessedPosition = checkpoint.CommitPosition;
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
}
