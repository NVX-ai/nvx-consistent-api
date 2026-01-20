using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Configuration.Modules;
using Nvx.ConsistentAPI.Configuration.Modules.Swagger;
using Nvx.ConsistentAPI.Framework.SignalRMessage;

namespace Nvx.ConsistentAPI;

public static class Generator
{
    /// <summary>
    /// Gets the default set of generator modules.
    /// </summary>
    public static IReadOnlyList<IGeneratorModule> GetDefaultModules(string[] corsOrigins) =>
    [
        new ValidationModule(),
        new DapperTypeHandlerModule(),
        new LoggingModule(),
        new AuthenticationModule(),
        new SwaggerModule(),
        new CorsModule(corsOrigins),
        new SignalRModule(),
        new TelemetryModule(),
        new MiddlewareModule()
    ];

    /// <summary>
    ///   Creates and configures a ConsistentApp instance.
    /// </summary>
    /// <param name="port">The port for the web application to listen on.</param>
    /// <param name="settings">The settings for the generator.</param>
    /// <param name="eventModel">The event model for the application.</param>
    /// <param name="corsOrigins">The allowed CORS origins.</param>
    /// <returns>A Task that results in a ConsistentApp instance.</returns>
    public static Task<ConsistentApp> GetWebApp(
        int? port,
        GeneratorSettings settings,
        EventModel eventModel,
        string[] corsOrigins) =>
        GetWebApp(port, settings, eventModel, corsOrigins, GetDefaultModules(corsOrigins));

    /// <summary>
    ///   Creates and configures a ConsistentApp instance with custom modules.
    /// </summary>
    /// <param name="port">The port for the web application to listen on.</param>
    /// <param name="settings">The settings for the generator.</param>
    /// <param name="eventModel">The event model for the application.</param>
    /// <param name="corsOrigins">The allowed CORS origins.</param>
    /// <param name="modules">The modules to use for configuration.</param>
    /// <returns>A Task that results in a ConsistentApp instance.</returns>
    public static async Task<ConsistentApp> GetWebApp(
        int? port,
        GeneratorSettings settings,
        EventModel eventModel,
        string[] corsOrigins,
        IReadOnlyList<IGeneratorModule> modules)
    {
        var builder = WebApplication.CreateBuilder();

        if (port != null)
        {
            builder.WebHost.UseUrls($"http://localhost:{port}");
        }

        // Configure services via modules (in order)
        foreach (var module in modules.OrderBy(m => m.Order))
        {
            module.ConfigureServices(builder, settings, eventModel);
        }

        // Apply builder customizations
        settings.BuilderCustomizations.Iter(c => c(builder));

        var app = builder.Build();

        // Configure app via modules (in order)
        foreach (var module in modules.OrderBy(m => m.Order))
        {
            module.ConfigureApp(app, settings, eventModel);
        }

        var logger = app.Services.GetRequiredService<ILogger<WebApplication>>();

        // Build merged event model
        var merged = BuildMergedEventModel(settings, eventModel, app, logger);

        VerifyPrefixes(merged);

        var (fetcher, consistencyCheck) = await merged.ApplyTo(app, settings, logger);

        // Apply app customizations
        settings.AppCustomizations.Iter(c => c(app));

        return new ConsistentApp(app, fetcher, consistencyCheck);
    }

    private static EventModel BuildMergedEventModel(
        GeneratorSettings settings,
        EventModel eventModel,
        WebApplication app,
        ILogger logger)
    {
        var merged = FrameworkEventModel
            .Model(settings)
            .Merge(eventModel);

        if (settings.EnabledFeatures.HasFlag(FrameworkFeatures.SignalR))
        {
            merged = merged.Merge(
                SignalRMessageSubModel.Get(
                    SendNotificationFunctionBuilder.Build(app.Services.GetRequiredService<IHubContext<NotificationHub>>())));
        }
        else
        {
            logger.LogInformation("Signal-R feature is disabled, not adding SignalR messages to the event model");
        }

        return merged;
    }

    private static void VerifyPrefixes(EventModel merged)
    {
        var prefixes = merged.Prefixes;
        foreach (var prefix in prefixes)
        {
            var overlappingPrefix = prefixes.FirstOrDefault(p => p != prefix && p.StartsWith(prefix));
            if (overlappingPrefix is not null)
            {
                throw new Exception($"Prefix '{overlappingPrefix}' overlaps with '{prefix}'");
            }
        }
    }

    /// <summary>
    /// Validates event cohesion. Exposed for backward compatibility.
    /// </summary>
    internal static void ValidateEventCohesion() => ValidationModule.ValidateEventCohesion();
}
