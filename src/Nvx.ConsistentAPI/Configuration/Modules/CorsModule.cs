using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Nvx.ConsistentAPI.Configuration.Modules;

/// <summary>
/// Module that configures CORS policies.
/// </summary>
public class CorsModule : IGeneratorModule
{
    private readonly string[] _corsOrigins;

    public CorsModule(string[] corsOrigins)
    {
        _corsOrigins = corsOrigins;
    }

    public int Order => 30;

    internal const string CorsPolicyName = "AllowDevelopmentCors";

    public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel)
    {
        builder.Services.AddCors(options =>
            options.AddPolicy(
                CorsPolicyName,
                policy =>
                    policy
                        .WithOrigins(_corsOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
            )
        );
    }

    public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel)
    {
        app.UseCors(CorsPolicyName);
        app.UseRouting();
    }
}
