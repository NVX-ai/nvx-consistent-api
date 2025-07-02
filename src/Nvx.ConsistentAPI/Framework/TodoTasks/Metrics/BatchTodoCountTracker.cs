namespace Nvx.ConsistentAPI.Metrics;

public class BatchTodoCountTracker : IDisposable
{
  private static readonly object DisposeLock = new();
  private readonly int batchSize;
  private bool isDisposed;

  public BatchTodoCountTracker(int batchSize)
  {
    this.batchSize = batchSize;
    PrometheusMetrics.RunningTodoBatch.Add(this.batchSize);
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

      PrometheusMetrics.RunningTodoBatch.Add(-batchSize);
      isDisposed = true;
    }
  }
}
