using System.Diagnostics;

namespace Nvx.ConsistentAPI.TestUtils;

public static class EventuallyConsistent
{
  public static async Task WaitFor(int ms, Func<Task> action)
  {
    var stopwatch = Stopwatch.StartNew();
    while (true)
    {
      try
      {
        await action();
        return;
      }
      catch
      {
        if (stopwatch.ElapsedMilliseconds > ms)
        {
          throw;
        }

        await Task.Delay(333);
      }
    }
  }

  public static async Task WaitFor(Func<Task> action) => await WaitFor(60_000, action);
}
