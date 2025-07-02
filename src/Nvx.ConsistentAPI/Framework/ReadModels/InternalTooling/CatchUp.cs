using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;

namespace Nvx.ConsistentAPI.InternalTooling;

internal static class CatchUp
{
  internal const string Route = "/read-models-hydration/hydration-status";

  internal static void Endpoint(
    EventModelingReadModelArtifact[] readModels,
    GeneratorSettings settings,
    Fetcher fetcher,
    Emitter emitter,
    WebApplication app)
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration)
        || !settings.EnabledFeatures.HasFlag(FrameworkFeatures.SystemEndpoints))
    {
      return;
    }

    Delegate catchupDelegate = async (HttpContext context) =>
    {
      var internalToolingApiKeyHeader =
        context.Request.Headers.TryGetValue("Internal-Tooling-Api-Key", out var internalToolingApiKey)
          ? internalToolingApiKey.ToString()
          : null;

      if (internalToolingApiKeyHeader == settings.ToolingEndpointsApiKey)
      {
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(new HydrationStatus(IsCaughtUp()));
        return;
      }

      await FrameworkSecurity
        .Authorization(context, fetcher, emitter, settings, new PermissionsRequireAll("admin"), None)
        .Iter(
          async _ =>
          {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new HydrationStatus(IsCaughtUp()));
          },
          async e => await e.Respond(context));
    };

    app
      .MapGet(Route, catchupDelegate)
      .WithName("read-models-hydration")
      .Produces<HydrationStatus>()
      .WithDescription("Checks if all read models have caught up")
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
    bool IsCaughtUp() => readModels.All(rm => rm.IsUpToDate());
  }
}
