using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Nvx.ConsistentAPI.Configuration.Modules
{
    /// <summary>
    /// Module that configures Serilog logging and Application Insights telemetry.
    /// </summary>
    public class LoggingModule : IGeneratorModule
    {
        public int Order => 5;

        public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel)
        {
            builder.Logging.SetMinimumLevel(settings.LoggingSettings.LogLevel);

            if (settings.LoggingSettings.AzureInstrumentationKey != null)
            {
                builder.Services.AddApplicationInsightsTelemetry(options =>
                {
                    options.ConnectionString = $"InstrumentationKey={settings.LoggingSettings.AzureInstrumentationKey}";
                });
            }

            builder.Services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.AddSerilog(
                    new LoggerConfiguration()
                        .MinimumLevel
                        .Override("Nvx.ConsistentAPI", MapLogLevel(settings.LoggingSettings.LogLevel))
                        .Enrich.FromLogContext()
                        .Filter.ByExcluding(logEvent =>
                            logEvent.Properties.TryGetValue("RequestPath", out var pathValue)
                            && pathValue.ToString().Contains("/metrics"))
                        .WriteTo.Conditional(
                            _ => settings.LoggingSettings.LogsFolder != null,
                            wt => wt.File(
                                Path.Combine(settings.LoggingSettings.LogsFolder ?? "./", "log-.log"),
                                rollingInterval: settings.LoggingSettings.LogFileRollInterval.ToSerilog(),
                                retainedFileTimeLimit: TimeSpan.FromDays(settings.LoggingSettings.LogDaysToKeep)))
                        .WriteTo.Conditional(
                            _ => settings.LoggingSettings.UseConsoleLogger,
                            wt => wt.Console(MapLogLevel(settings.LoggingSettings.LogLevel)))
                        .CreateLogger());
            });
        }

        public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel)
        {
            // No app configuration needed
        }

        private static LogEventLevel MapLogLevel(LogLevel level) => level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            _ => LogEventLevel.Fatal
        };
    }
}
