using EventStore.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.Framework.Projections;

// ReSharper disable NotAccessedPositionalProperty.Global

namespace Nvx.ConsistentAPI.InternalTooling;

internal static class DaemonsInsight
{
  internal const string Route = "/daemons-insight";

  internal static void Endpoint(
    EventModelingReadModelArtifact[] readModels,
    GeneratorSettings settings,
    EventStoreClient eventStoreClient,
    Fetcher fetcher,
    Emitter emitter,
    WebApplication app,
    ReadModelHydrationDaemon daemon,
    TodoProcessor processor,
    DynamicConsistencyBoundaryDaemon dcbDaemon,
    ProjectionDaemon projectionDaemon,
    ILogger logger)
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.SystemEndpoints))
    {
      return;
    }

    Delegate catchupDelegate = async (HttpContext context) =>
    {
      try
      {
        var internalToolingApiKeyHeader =
          context.Request.Headers.TryGetValue("Internal-Tooling-Api-Key", out var internalToolingApiKey)
            ? internalToolingApiKey.ToString()
            : null;

        if (internalToolingApiKeyHeader == settings.ToolingEndpointsApiKey)
        {
          context.Response.StatusCode = StatusCodes.Status200OK;
          await context.Response.WriteAsJsonAsync(
            await GetDaemonInsights(
              settings,
              processor,
              readModels,
              eventStoreClient,
              daemon,
              dcbDaemon,
              projectionDaemon));
          return;
        }

        await FrameworkSecurity
          .Authorization(context, fetcher, emitter, settings, new PermissionsRequireAll("admin"), None)
          .Iter(
            async _ =>
            {
              context.Response.StatusCode = StatusCodes.Status200OK;
              await context.Response.WriteAsJsonAsync(
                await GetDaemonInsights(
                  settings,
                  processor,
                  readModels,
                  eventStoreClient,
                  daemon,
                  dcbDaemon,
                  projectionDaemon));
            },
            async e => await e.Respond(context));
      }
      catch (Exception ex)
      {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { ex.Message });
        logger.LogError(ex, "Failed getting insights from the daemons");
      }
    };

    app
      .MapGet(Route, catchupDelegate)
      .WithName("daemons-insight")
      .Produces<DaemonsInsights>()
      .ProducesProblem(500)
      .WithDescription("Gives insights into the system daemons")
      .WithOpenApi(o =>
      {
        o.Tags = [new OpenApiTag { Name = OperationTags.FrameworkManagement }];
        o.Parameters.Add(
          new OpenApiParameter
          {
            In = ParameterLocation.Header,
            Name = "Internal-Tooling-Api-Key",
            Required = false,
            Schema = new OpenApiSchema { Type = "string" },
            Description = "API key for accessing tooling endpoints, not required for admin users"
          });

        return o;
      })
      .ApplyAuth(new PermissionsRequireAll("admin"));
  }

  private static async Task<DaemonsInsights> GetDaemonInsights(
    GeneratorSettings settings,
    TodoProcessor processor,
    EventModelingReadModelArtifact[] readModels,
    EventStoreClient eventStoreClient,
    ReadModelHydrationDaemon readModelDaemon,
    DynamicConsistencyBoundaryDaemon dynamicConsistencyBoundaryDaemon,
    ProjectionDaemon projectionDaemon)
  {
    var isHydrating = settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration);
    var lastEventPosition = 0UL;
    var lastEventEmittedAt = DateTime.UnixEpoch;
    await foreach (var evt in eventStoreClient
                     .ReadAllAsync(Direction.Backwards, Position.End, EventTypeFilter.ExcludeSystemEvents(), 1)
                     .Take(1))
    {
      lastEventPosition = evt.Event.Position.CommitPosition;
      lastEventEmittedAt = evt.Event.Created;
    }

    var catchingUpReadModels = isHydrating
      ? await readModels
        .Select<EventModelingReadModelArtifact, Func<Task<SingleReadModelInsights>>>(rm =>
          async () => await rm.Insights(lastEventPosition, eventStoreClient))
        .Parallel()
        .Map(i =>
          i
            .Where(s => s.PercentageComplete < 100)
            .OrderBy(s => s.PercentageComplete)
            .ThenBy(s => s.ModelName)
            .ToArray())
      : [];

    return new DaemonsInsights(
      catchingUpReadModels,
      new ReadModelsInsights(
        isHydrating ? readModels.Length : 0,
        isHydrating ? readModels.Length - catchingUpReadModels.Length : 0),
      await readModelDaemon.GetLingeringFailedHydrations(),
      processor.RunningTodoTasks,
      projectionDaemon.Insights(lastEventPosition),
      isHydrating ? await readModelDaemon.Insights(lastEventPosition) : null,
      dynamicConsistencyBoundaryDaemon.Insights(lastEventPosition),
      lastEventEmittedAt,
      lastEventPosition);
  }
}

public record DaemonsInsights(
  SingleReadModelInsights[] CatchingUpReadModels,
  ReadModelsInsights ReadModels,
  FailedHydration[] FailedHydrations,
  RunningTodoTaskInsight[] Tasks,
  ProjectorDaemonInsights ProjectorDaemon,
  HydrationDaemonInsights? HydrationDaemonInsights,
  DynamicConsistencyBoundaryDaemonInsights DynamicConsistencyBoundaryDaemonInsights,
  DateTime LastEventEmittedAt,
  ulong LastEventPosition)
{
  public bool AreReadModelsUpToDate =>
    CatchingUpReadModels.All(rm => rm.PercentageComplete == 100)
    && ReadModels.Total == ReadModels.UpToDate
    && FailedHydrations.Length == 0
    && (HydrationDaemonInsights is null
        || (HydrationDaemonInsights.PercentageComplete == 100 && HydrationDaemonInsights.EventsBeingProcessed == 0));

  public bool AreDaemonsIdle =>
    ProjectorDaemon is { PercentageComplete: 100, CatchUpPercentageComplete: 100, IsProjecting: false }
    && DynamicConsistencyBoundaryDaemonInsights.CurrentPercentageComplete == 100;

  public bool IsFullyIdle => AreDaemonsIdle && AreReadModelsUpToDate && Tasks.Length == 0;
}

public record ReadModelsInsights(int Total, int UpToDate);

public record RunningTodoTaskInsight(string TaskType, string[] RelatedEntityIds);

public record HydrationDaemonInsights(
  ulong LastProcessedPosition,
  ulong LastCheckpoint,
  decimal PercentageComplete,
  int EventsBeingProcessed);

public record DynamicConsistencyBoundaryDaemonInsights(
  ulong CurrentProcessedPosition,
  decimal CurrentPercentageComplete,
  ulong? CurrentSweepPosition,
  bool IsSweepFinished,
  int TotalInterestsRegisteredSinceStartup,
  int TotalInterestsRemovedSinceStartup,
  decimal SweepPercentageComplete);

public record SingleReadModelInsights(
  string ModelName,
  ulong? LastProcessedEventPosition,
  ulong? CheckpointPosition,
  bool IsAggregating,
  decimal PercentageComplete);

public record ProjectorDaemonInsights(
  ulong? DaemonLastEventProjected,
  decimal PercentageComplete,
  string[] CatchingUpProjections,
  ulong? CatchUpLastPositionProcessed,
  decimal CatchUpPercentageComplete,
  int EventsProjectedSinceStartup,
  bool IsProjecting);
