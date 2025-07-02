namespace Nvx.ConsistentAPI.Metrics;

public class RunningTodoCountTracker : IDisposable
{
  private static readonly object DisposeLock = new();
  private bool isDisposed;

  public RunningTodoCountTracker()
  {
    PrometheusMetrics.RunningTodos.Add(1);
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

      PrometheusMetrics.RunningTodos.Add(-1);
      isDisposed = true;
    }
  }
}
