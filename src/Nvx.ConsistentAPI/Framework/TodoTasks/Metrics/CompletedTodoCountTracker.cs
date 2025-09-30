namespace Nvx.ConsistentAPI.Metrics;

public class CompletedTodoCountTracker : IDisposable
{
  private static readonly object DisposeLock = new();
  private bool isDisposed;

  public CompletedTodoCountTracker(string name) => PrometheusMetrics.AddCompletedTodoCount(name);

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
