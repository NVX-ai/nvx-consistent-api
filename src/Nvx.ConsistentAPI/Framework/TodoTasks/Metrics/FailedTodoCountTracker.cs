namespace Nvx.ConsistentAPI.Metrics;

public class FailedTodoCountTracker : IDisposable
{
  private static readonly object DisposeLock = new();
  private bool isDisposed;

  public FailedTodoCountTracker(string name) => PrometheusMetrics.AddFailedTodoCount(name);

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
