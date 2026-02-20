using Nvx.ConsistentAPI.InternalTooling;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Thread-safe tracker that maintains a list of currently executing todo tasks.
/// Used to prevent duplicate processing of the same task type and entity.
/// </summary>
internal class RunningTodoTracker
{
  private readonly SemaphoreSlim semaphore = new(1, 1);
  private readonly List<RunningTodoTaskInsight> runningTodoTasks = [];

  internal async Task<RunningTodoTaskInsight[]> GetRunningTodoTasks()
  {
    await semaphore.WaitAsync();
    try
    {
      return runningTodoTasks
        .GroupBy(rtt => rtt.TaskType)
        .Select(g => new RunningTodoTaskInsight(g.Key, g.SelectMany(rtt => rtt.RelatedEntityIds).ToArray()))
        .ToArray();
    }
    finally
    {
      semaphore.Release();
    }
  }

  internal async Task<RunningTodoTaskInsight[]> GetCurrentlyRunning()
  {
    await semaphore.WaitAsync();
    try
    {
      return runningTodoTasks.ToArray();
    }
    finally
    {
      semaphore.Release();
    }
  }

  internal async Task<RunningTodoTaskInsight> Add(string taskType, string relatedEntityId)
  {
    await semaphore.WaitAsync();
    try
    {
      var insight = new RunningTodoTaskInsight(taskType, [relatedEntityId]);
      runningTodoTasks.Add(insight);
      return insight;
    }
    finally
    {
      semaphore.Release();
    }
  }

  internal async Task Remove(RunningTodoTaskInsight? insight)
  {
    if (insight is null) return;

    await semaphore.WaitAsync();
    try
    {
      runningTodoTasks.RemoveAll(rtt =>
        rtt.TaskType == insight.TaskType
        && insight.RelatedEntityIds.All(id => rtt.RelatedEntityIds.Contains(id)));
    }
    finally
    {
      semaphore.Release();
    }
  }
}
