using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;

namespace Nvx.ConsistentAPI.InternalTooling;

internal static class PreHydrationCompleted
{
  internal const string Route = "/pre-hydration-completed";

  internal static void Endpoint(
    EventModelingReadModelArtifact[] readModels,
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
        // For HTTP probes we need to return an error HTTP code if not caught up
        context.Response.StatusCode = IsCaughtUp() ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new PreHydrationStatus(IsCaughtUp(), hydratedAt));
        return;
      }

      await FrameworkSecurity
        .Authorization(context, fetcher, emitter, settings, new PermissionsRequireAll("admin"), None)
        .Iter(
          async _ =>
          {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new PreHydrationStatus(IsCaughtUp(), hydratedAt));
          },
          async e => await e.Respond(context));
    };

    app
      .MapGet(Route, preHydrationDelegate)
      .WithName("read-models-pre-hydration")
      .Produces<PreHydrationStatus>()
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
    bool IsCaughtUp()
    {
      if (hydratedAt.HasValue)
      {
        return true;
      }

      var isCaughtUp = readModels.All(rm => rm.IsUpToDate());
      if (isCaughtUp)
      {
        hydratedAt = DateTime.UtcNow;
      }

      return isCaughtUp;
    }
  }
}

public record PreHydrationStatus(bool HasReachedConsistencyOnce, DateTime? ReachedFirstConsistencyAt);
