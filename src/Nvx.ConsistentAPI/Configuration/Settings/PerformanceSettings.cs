namespace Nvx.ConsistentAPI.Configuration.Settings;

/// <summary>
/// Settings for performance tuning (parallelism, worker counts).
/// </summary>
public record PerformanceSettings(
    int ParallelHydration = 25,
    int TodoProcessorWorkerCount = 25);
