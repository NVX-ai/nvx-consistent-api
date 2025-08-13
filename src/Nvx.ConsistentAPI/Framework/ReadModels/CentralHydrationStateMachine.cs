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
      return hydrationTasks.Count(t => !t.task.IsCompleted);
    }
    finally
    {
      clearanceSemaphore.Release();
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
    }
    finally
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
