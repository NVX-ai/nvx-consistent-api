using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace ConsistentAPI;

internal class CentralHydrationStateMachine(GeneratorSettings settings, ILogger logger)
{
  private readonly SemaphoreSlim clearanceSemaphore = new(1, 1);
  private readonly SemaphoreSlim hydrationSemaphore = new(settings.ParallelHydration, settings.ParallelHydration);
  private readonly List<(string stream, Task task)> hydrationTasks = [];

  public async Task Queue(ResolvedEvent evt, Func<ResolvedEvent, Task> tryProcess)
  {
    await hydrationSemaphore.WaitAsync();
    if (hydrationTasks.Any(t => t.stream == evt.Event.EventStreamId))
    {
      try
      {
        await clearanceSemaphore.WaitAsync();
        await Task.WhenAll(hydrationTasks.Select(t => t.task));
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Error on hydration, they have an internal retry mechanism, so this is not critical");
      }
      finally
      {
        hydrationTasks.Clear();
        clearanceSemaphore.Release();
      }
    }

    hydrationTasks.Add((evt.Event.EventStreamId, DoQueue(evt, tryProcess)));
  }

  private async Task DoQueue(ResolvedEvent evt, Func<ResolvedEvent, Task> tryProcess)
  {
    try
    {
      await tryProcess(evt);
    }
    finally
    {
      hydrationSemaphore.Release();
    }
  }

  public async Task Checkpoint(Position position, Func<Position, Task> checkpoint)
  {
    try
    {
      await clearanceSemaphore.WaitAsync();
      await Task.WhenAll(hydrationTasks.Select(t => t.task));
      await checkpoint(position);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Error on hydration, they have an internal retry mechanism, so this is not critical");
    }
    finally
    {
      hydrationTasks.Clear();
      clearanceSemaphore.Release();
    }
  }
}
