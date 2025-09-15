using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI;

internal class CentralHydrationStateMachine(GeneratorSettings settings, ILogger logger)
{
  private readonly SemaphoreSlim clearanceSemaphore = new(1, 1);
  private readonly SemaphoreSlim hydrationSemaphore = new(settings.ParallelHydration, settings.ParallelHydration);
  private readonly List<(string stream, Task task)> hydrationTasks = [];

  public async Task<int> ProcessingCount()
  {
    await clearanceSemaphore.WaitAsync();
    var result = hydrationTasks.Count(t => !t.task.IsCompleted);
    clearanceSemaphore.Release();
    return result;
  }

  public async Task Queue(ResolvedEvent evt, Func<ResolvedEvent, Task> tryProcess)
  {
    await hydrationSemaphore.WaitAsync();
    await clearanceSemaphore.WaitAsync();

    var tasksForSameStream =
      hydrationTasks.Where(t => t.stream == evt.Event.EventStreamId).Select(t => t.task).ToArray();

    if (tasksForSameStream.Length > 0)
    {
      hydrationTasks.Add(
        (evt.Event.EventStreamId, Task.Run(async () =>
        {
          try
          {
            await Task.WhenAll(tasksForSameStream);
          }
          catch
          {
            // ignore
          }

          await DoQueue(evt, tryProcess);
        })));
      clearanceSemaphore.Release();
      return;
    }

    hydrationTasks.Add((evt.Event.EventStreamId, DoQueue(evt, tryProcess)));
    clearanceSemaphore.Release();
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
