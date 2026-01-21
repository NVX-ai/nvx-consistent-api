using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI.Logging;

public class LoggingSettings
{
  public string? LogsFolder { get; init; }
  public string? AzureInstrumentationKey { get; init; }
  public LogLevel LogLevel { get; init; } = LogLevel.Trace;
  public bool UseConsoleLogger { get; init; } = true;
  public string? TracingOpenTelemetryEndpoint { get; init; }
  public LogFileRollInterval LogFileRollInterval { get; init; } = LogFileRollInterval.Hour;
  public int LogDaysToKeep { get; init; } = 7;
  public bool AddMetricsAndTracingToConsole { get; init; } = false;
}
