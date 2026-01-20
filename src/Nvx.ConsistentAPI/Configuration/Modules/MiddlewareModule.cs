using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;

namespace Nvx.ConsistentAPI.Configuration.Modules;

/// <summary>
/// Module that configures custom middleware for file uploads and Swagger CORS.
/// </summary>
public class MiddlewareModule : IGeneratorModule
{
    public int Order => 100;

    public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel)
    {
        // No service configuration needed
    }

    public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel)
    {
        app.Use((context, next) =>
        {
            if (
                context.Request.Path.ToString().EndsWith("files/upload")
                && context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { } feature
            )
            {
                feature.MaxRequestBodySize = 350_000_000;
            }

            if (context.Request.Path.ToString().EndsWith("swagger.json"))
            {
                context.Response.Headers.AccessControlAllowOrigin = "*";
            }

            return next.Invoke();
        });
    }
}
