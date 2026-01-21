using KurrentDB.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.Framework.Projections.Model;
using Nvx.ConsistentAPI.InternalTooling;
using Nvx.ConsistentAPI.Metrics;

namespace Nvx.ConsistentAPI.Framework.Projections;

/// <summary>
/// Main orchestrator for the projection system. Coordinates initialization, subscription
/// management, catch-up processing, and provides monitoring insights.
/// </summary>
/// <remarks>
/// The projection daemon manages the lifecycle of projections:
/// 1. Initialization: Sets up projection tracker state and registers projections
/// 2. Subscription: Maintains a live subscription for real-time event processing
/// 3. Catch-up: Processes historical events for projections that are behind
/// 4. Monitoring: Provides insights into projection progress and status
/// 5. Administration: Exposes endpoint for resetting/rerunning projections
///
/// The daemon delegates actual processing to specialized handlers:
/// - <see cref="ProjectionSubscriptionHandler"/> for live event subscription
/// - <see cref="ProjectionCatchUpHandler"/> for historical event processing
/// - <see cref="ProjectionSetupOperations"/> for initialization operations
/// </remarks>
/// <param name="projectors">Array of all projection artifacts to be managed.</param>
/// <param name="fetcher">Fetcher instance for retrieving entities.</param>
/// <param name="emitter">Emitter instance for emitting events.</param>
/// <param name="client">KurrentDB client for event store operations.</param>
/// <param name="parser">Function to parse resolved events into domain events.</param>
/// <param name="app">ASP.NET Core application for registering management endpoints.</param>
/// <param name="gs">Generator settings containing feature flags.</param>
/// <param name="logger">Logger for diagnostic output.</param>
public class ProjectionDaemon(
  EventModelingProjectionArtifact[] projectors,
  Fetcher fetcher,
  Emitter emitter,
  KurrentDBClient client,
  Func<ResolvedEvent, Option<EventModelEvent>> parser,
  WebApplication app,
  GeneratorSettings gs,
  ILogger logger)
{
  private readonly ProjectionDaemonState state = new();

  /// <summary>
  /// Generates current insights about the projection daemon's status and progress.
  /// Used for monitoring and health checks.
  /// </summary>
  /// <param name="lastEventPosition">
  /// The current last position in the event store, used to calculate progress percentages.
  /// </param>
  /// <returns>
  /// Insights object containing:
  /// - Current subscription position and progress percentage
  /// - Catch-up status and progress percentage
  /// - Total projected event count
  /// - Active processing indicator
  /// </returns>
  public ProjectorDaemonInsights Insights(ulong lastEventPosition)
  {
    var daemonPercentage = lastEventPosition == 0
      ? 100m
      : Convert.ToDecimal(state.LastProcessedPosition) * 100m / Convert.ToDecimal(lastEventPosition);
    var catchUpPercentage = lastEventPosition == 0 || state.CatchingUp.Length == 0
      ? 100m
      : Convert.ToDecimal(state.LastCatchUpProcessedPosition) * 100m / Convert.ToDecimal(lastEventPosition);

    return new ProjectorDaemonInsights(
      state.LastProcessedPosition,
      Math.Min(100m, daemonPercentage),
      state.CatchingUp,
      state.LastCatchUpProcessedPosition,
      Math.Min(100m, catchUpPercentage),
      state.ProjectedCount,
      state.IsProjecting);
  }

  /// <summary>
  /// Initializes the projection daemon by setting up state, starting handlers,
  /// and registering management endpoints.
  /// </summary>
  /// <remarks>
  /// Initialization sequence:
  /// 1. Check if Projections feature is enabled
  /// 2. Perform first-time setup (emit initial snapshot if needed)
  /// 3. Register any new projections added since last startup
  /// 4. Start the live subscription handler (fire-and-forget)
  /// 5. Start the catch-up handler for behind projections (fire-and-forget)
  /// 6. Register the /rerun-projection endpoint for manual projection resets
  ///
  /// The subscription and catch-up handlers run concurrently. The subscription
  /// processes new events in real-time while catch-up processes historical events
  /// for projections that are behind.
  /// </remarks>
  public async Task Initialize()
  {
    if (!gs.EnabledFeatures.HasFlag(FrameworkFeatures.Projections))
    {
      return;
    }

    var projectionNames = projectors.Select(p => p.Name).ToArray();
    var catchUpHandler = new ProjectionCatchUpHandler(projectors, fetcher, emitter, client, parser, state, logger);
    var subscriptionHandler = new ProjectionSubscriptionHandler(projectors, fetcher, emitter, client, parser, state, gs, logger);

    await ProjectionSetupOperations.FirstSetup(fetcher, emitter, projectionNames);
    await ProjectionSetupOperations.RegisterNewProjections(fetcher, emitter, projectionNames);
    _ = subscriptionHandler.Subscribe();
    _ = catchUpHandler.CatchUp();

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
              await ResetProjection(projectionName, catchUpHandler);
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
  }

  /// <summary>
  /// Resets a projection by emitting a reset event and triggering catch-up.
  /// The projection will reprocess all historical events from the beginning.
  /// </summary>
  /// <param name="projectionName">The name of the projection to reset.</param>
  /// <param name="catchUpHandler">The catch-up handler to trigger after reset.</param>
  /// <remarks>
  /// Because projections use idempotent UUID generation, rerunning a projection
  /// is safe - duplicate events will be rejected by the event store.
  /// </remarks>
  private async Task ResetProjection(string projectionName, ProjectionCatchUpHandler catchUpHandler)
  {
    await emitter.Emit(() => new AnyState(new ProjectionReset(ProjectionDaemonState.SubscriptionVersion, projectionName)));
    logger.LogInformation("Resetting projection {ProjectionName}", projectionName);
    _ = catchUpHandler.CatchUp();
  }
}
