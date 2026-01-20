using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Nvx.ConsistentAPI.Configuration.Modules;

/// <summary>
/// Module that configures OpenTelemetry metrics and tracing.
/// </summary>
public class TelemetryModule : IGeneratorModule
{
    public int Order => 50;

    public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel)
    {
        Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", eventModel.ApiName ?? "Nvx.ConsistentAPI");

        var otel = builder.Services.AddOpenTelemetry();
        otel.WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter("Microsoft.AspNetCore.Hosting")
                .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                .AddMeter("System.Net.Http")
                .AddMeter("System.Net.NameResolution")
                .AddMeter(PrometheusMetrics.Source.Name)
                .AddMeter("System.Runtime")
                .AddPrometheusExporter();

            if (settings.LoggingSettings is { UseConsoleLogger: true, AddMetricsAndTracingToConsole: true })
            {
                metrics.AddConsoleExporter();
            }

            if (settings.LoggingSettings.TracingOpenTelemetryEndpoint != null)
            {
                metrics.AddOtlpExporter(otlpOptions =>
                {
                    otlpOptions.Endpoint = new Uri(settings.LoggingSettings.TracingOpenTelemetryEndpoint);
                });
            }
        });

        otel.WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation();
            tracing.AddHttpClientInstrumentation();
            tracing.AddSource(PrometheusMetrics.Source.Name);
            if (settings.LoggingSettings.TracingOpenTelemetryEndpoint != null)
            {
                tracing.AddOtlpExporter(otlpOptions =>
                {
                    otlpOptions.Endpoint = new Uri(settings.LoggingSettings.TracingOpenTelemetryEndpoint);
                });
            }

            if (settings.LoggingSettings is { UseConsoleLogger: true, AddMetricsAndTracingToConsole: true })
            {
                tracing.AddConsoleExporter();
            }
        });
    }

    public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel)
    {
        app.MapPrometheusScrapingEndpoint();
    }
}
