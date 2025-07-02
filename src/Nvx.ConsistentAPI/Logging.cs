using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Nvx.ConsistentAPI;

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

public enum LogFileRollInterval
{
  Infinite,
  Year,
  Month,
  Day,
  Hour,
  Minute
}

internal static class LogFileRollIntervalExtensions
{
  internal static RollingInterval ToSerilog(this LogFileRollInterval interval) =>
    interval switch
    {
      LogFileRollInterval.Year => RollingInterval.Year,
      LogFileRollInterval.Month => RollingInterval.Month,
      LogFileRollInterval.Day => RollingInterval.Day,
      LogFileRollInterval.Hour => RollingInterval.Hour,
      LogFileRollInterval.Minute => RollingInterval.Minute,
      _ => RollingInterval.Infinite
    };
}

internal static class PrometheusMetrics
{
  internal static readonly ActivitySource Source = new("Nvx.ConsistentAPI", "1.0.0");
  private static readonly Meter Meter = new("Nvx.ConsistentAPI", "1.0.0");

  internal static readonly UpDownCounter<int> CatchUpHydration =
    Meter.CreateUpDownCounter<int>("read_models.catching_up");

  internal static readonly UpDownCounter<int> RunningTodos =
    Meter.CreateUpDownCounter<int>("todo_tasks.running_todos");

  internal static readonly UpDownCounter<int> RunningTodoBatch =
    Meter.CreateUpDownCounter<int>("todo_tasks.running_todo_batch");
}
