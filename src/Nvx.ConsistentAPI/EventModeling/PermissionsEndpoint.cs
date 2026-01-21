using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nvx.ConsistentAPI.Configuration.Settings;
using Nvx.ConsistentAPI.InternalTooling;

namespace Nvx.ConsistentAPI.EventModeling;

public static class PermissionsEndpoint
{
  public static void ApplyTo(this string[] permissions, WebApplication app, GeneratorSettings settings)
  {
    if (!settings.EnabledFeatures.HasEndpoints())
    {
      return;
    }

    app
      .MapGet("/permissions", () => permissions)
      .WithTags(OperationTags.Authorization)
      .WithOpenApi(o =>
      {
        o.Description = "Returns a list of all the permissions used in the application.";
        return o;
      });
  }
}
