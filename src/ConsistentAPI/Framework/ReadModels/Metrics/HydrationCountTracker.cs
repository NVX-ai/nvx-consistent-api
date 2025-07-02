namespace ConsistentAPI;

public class HydrationCountTracker : IDisposable
{
  private bool isDisposed;
  private static readonly object DisposeLock = new();
  private readonly int hydrationCount;

  public HydrationCountTracker(int count = 1)
  {
    hydrationCount = count;
    PrometheusMetrics.CatchUpHydration.Add(hydrationCount);
  }

  public void Dispose()
  {
    GC.SuppressFinalize(this);
    lock (DisposeLock)
    {
      if (isDisposed)
      {
        return;
      }

      PrometheusMetrics.CatchUpHydration.Add(-hydrationCount);
      isDisposed = true;
    }
  }
}
