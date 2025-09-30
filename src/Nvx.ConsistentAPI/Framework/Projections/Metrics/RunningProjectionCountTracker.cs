namespace Nvx.ConsistentAPI.Metrics;

public class RunningProjectionCountTracker : IDisposable
{
  private static readonly object DisposeLock = new();
  private bool isDisposed;

  public RunningProjectionCountTracker(string name) => PrometheusMetrics.AddRunningProjectionsCount(name);

  public void Dispose()
  {
    GC.SuppressFinalize(this);
    lock (DisposeLock)
    {
      if (isDisposed)
      {
        return;
      }
      isDisposed = true;
    }
  }
}
