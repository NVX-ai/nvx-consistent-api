using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;

namespace Nvx.ConsistentAPI.InternalTooling;

internal static class PreHydrationCompleted
{
  internal const string Route = "/pre-hydration-completed";

  internal static void Endpoint(
    EventModelingReadModelArtifact[] readModels,
    ReadModelHydrationDaemon centralDaemon,
    GeneratorSettings settings,
    Fetcher fetcher,
    Emitter emitter,
    WebApplication app)
  {
    DateTime? hydratedAt = null;
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration)
        || !settings.EnabledFeatures.HasFlag(FrameworkFeatures.SystemEndpoints))
    {
      return;
    }

    Delegate preHydrationDelegate = async (HttpContext context) =>
    {
      var internalToolingApiKeyHeader =
        context.Request.Headers.TryGetValue("Internal-Tooling-Api-Key", out var internalToolingApiKey)
          ? internalToolingApiKey.ToString()
          : null;

      if (internalToolingApiKeyHeader == settings.ToolingEndpointsApiKey)
      {
        // This endpoint is meant to be consumed by tooling that relies on status codes.
        var (isCentral, behind) = IsCaughtUp();
        var isCaughtUp = isCentral && behind.Length == 0;
        context.Response.StatusCode = isCaughtUp ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new PreHydrationStatus(isCaughtUp, hydratedAt, isCentral, behind));
        return;
      }

      await FrameworkSecurity
        .Authorization(context, fetcher, emitter, settings, new PermissionsRequireAll("admin"), None)
        .Iter(
          async _ =>
          {
            var (isCentral, behind) = IsCaughtUp();
            var isCaughtUp = isCentral && behind.Length == 0;
            context.Response.StatusCode =
              isCaughtUp ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new PreHydrationStatus(isCaughtUp, hydratedAt, isCentral, behind));
          },
          async e => await e.Respond(context));
    };

    app
      .MapGet(Route, preHydrationDelegate)
      .WithName("read-models-pre-hydration")
      .Produces<PreHydrationStatus>()
      .Produces<PreHydrationStatus>(StatusCodes.Status503ServiceUnavailable)
      .WithDescription("Checks if all read models have ever been caught up")
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

    return;

    (bool centralUpToDate, string[] behind) IsCaughtUp()
    {
      if (hydratedAt.HasValue)
      {
        return (true, []);
      }

      var behind = readModels.Where(rm => !rm.IsUpToDate()).Select(rm => rm.GetType().Name).ToArray();
      var centralDaemonUpToDate = centralDaemon.IsUpToDate(null);

      var isCaughtUp = behind.Length == 0 && centralDaemonUpToDate;
      if (isCaughtUp)
      {
        hydratedAt = DateTime.UtcNow;
      }

      return (centralDaemonUpToDate, behind);
    }
  }
}

public record PreHydrationStatus(
  bool HasReachedConsistencyOnce,
  DateTime? ReachedFirstConsistencyAt,
  bool IsCentralDaemonUpToDate,
  string[] ReadModelsNotUpToDate);