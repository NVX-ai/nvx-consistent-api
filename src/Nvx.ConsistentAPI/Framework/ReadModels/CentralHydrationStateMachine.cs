using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI;

internal class CentralHydrationStateMachine(GeneratorSettings settings, ILogger logger)
{
  private readonly SemaphoreSlim clearanceSemaphore = new(1, 1);
  private readonly SemaphoreSlim hydrationSemaphore = new(settings.ParallelHydration, settings.ParallelHydration);
  private readonly List<(string stream, Task task)> hydrationTasks = [];

  public async Task<int> EventsBeingProcessedCount()
  {
    try
    {
      await clearanceSemaphore.WaitAsync();
      var running = hydrationTasks.Count(t => !t.task.IsCompleted);
      var queued = settings.ParallelHydration - hydrationSemaphore.CurrentCount;
      clearanceSemaphore.Release();
      return queued + running;
    }
    catch
    {
      clearanceSemaphore.Release();
      return 0;
    }
  }

  public async Task Queue(
    StrongId strongId,
    StoredEventMetadata metadata,
    EventModelEvent evt,
    Func<StrongId, StoredEventMetadata, EventModelEvent, Task> tryProcess)
  {
    await hydrationSemaphore.WaitAsync();
    if (hydrationTasks.Any(t => t.stream == evt.GetStreamName()))
    {
      try
      {
        await clearanceSemaphore.WaitAsync();
        await Task.WhenAll(hydrationTasks.Select(t => t.task));
        hydrationTasks.Clear();
        clearanceSemaphore.Release();
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Error on hydration, they have an internal retry mechanism, so this is not critical");
        hydrationTasks.Clear();
        clearanceSemaphore.Release();
      }
    }

    hydrationTasks.Add((evt.GetStreamName(), DoQueue(strongId, metadata, evt, tryProcess)));
  }

  private async Task DoQueue(
    StrongId strongId,
    StoredEventMetadata metadata,
    EventModelEvent evt,
    Func<StrongId, StoredEventMetadata, EventModelEvent, Task> tryProcess)
  {
    try
    {
      await tryProcess(strongId, metadata, evt);
      hydrationSemaphore.Release();
    }
    catch
    {
      hydrationSemaphore.Release();
    }
  }

  public async Task Checkpoint(ulong position, Func<ulong, Task> checkpoint)
  {
    try
    {
      await clearanceSemaphore.WaitAsync();
      await Task.WhenAll(hydrationTasks.Select(t => t.task));
      await checkpoint(position);
      hydrationTasks.Clear();
      clearanceSemaphore.Release();
    }
    catch (Exception ex)
    {
      hydrationTasks.Clear();
      clearanceSemaphore.Release();
      logger.LogWarning(ex, "Error on hydration, they have an internal retry mechanism, so this is not critical");
    }
  }
}
