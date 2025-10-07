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

  internal static readonly UpDownCounter<int> RunningTodoBatch =
    Meter.CreateUpDownCounter<int>("todo_tasks.running_todo_batch");

  private static readonly Counter<int> ReadModelEventsProcessedCounter =
    Meter.CreateCounter<int>("read_models.events_processed_total");
  internal static void AddReadModelEventsProcessed(string readModelName) =>
    ReadModelEventsProcessedCounter.Add(1, new TagList { { "readmodel", readModelName } });

  private static readonly Counter<int> ReadModelItemsCached =
    Meter.CreateCounter<int>("read_models.items_cached_total");
  
  internal static void AddReadModelItemsCached(string readModelName, int delta) =>
    ReadModelItemsCached.Add(delta, new TagList { { "readmodel", readModelName } });
  
  private static readonly Counter<int> RunningTodos =
    Meter.CreateCounter<int>("todo_tasks.running_todos");

  internal static void AddRunnningTodoCount(string name) =>
    RunningTodos.Add(1, new TagList { { "name", name } });
  
  private static readonly Counter<int> FailedTodos =
    Meter.CreateCounter<int>("todo_tasks.failed_todos");

  internal static void AddFailedTodoCount(string name) =>
    FailedTodos.Add(1, new TagList { { "name", name } });
  
  private static readonly Counter<int> FailedRetryTodos =
    Meter.CreateCounter<int>("todo_tasks.failed_retry_todos");

  internal static void AddFailedRetryTodoCount(string name) =>
    FailedRetryTodos.Add(1, new TagList { { "name", name } });
  
  private static readonly Counter<int> CompletedTodos =
    Meter.CreateCounter<int>("todo_tasks.completed_todos");

  internal static void AddCompletedTodoCount(string name) =>
    CompletedTodos.Add(1, new TagList { { "name", name } });
  
  private static readonly Counter<int> RunningProjections =
    Meter.CreateCounter<int>("projections.running_projections");

  internal static void AddRunningProjectionsCount(string name) =>
    RunningProjections.Add(1, new TagList { { "name", name } });
  
  private static readonly Histogram<double> TodoProcessingTime =
    Meter.CreateHistogram<double>("todo_tasks.processing_time_ms", "ms");
  internal static void RecordTodoProcessingTime(string name, double milliseconds) =>
    TodoProcessingTime.Record(milliseconds, new TagList { { "name", name } });
  
  private static readonly Histogram<double> AggregatingProcessingTime =
    Meter.CreateHistogram<double>("read_models.aggregating_time_ms", "ms");
  internal static void RecordAggregatingProcessingTime(string readModelName, double milliseconds) =>
    AggregatingProcessingTime.Record(milliseconds, new TagList { { "readmodel", readModelName } });
  
  private static readonly Histogram<double> ReadModelProcessingTime =
    Meter.CreateHistogram<double>("read_models.processing_time_ms", "ms");
  internal static void RecordReadModelProcessingTime(string readModelName, double milliseconds) =>
    ReadModelProcessingTime.Record(milliseconds, new TagList { { "readmodel", readModelName } });
}
