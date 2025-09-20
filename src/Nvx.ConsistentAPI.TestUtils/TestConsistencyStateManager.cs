using System.Diagnostics;
using EventStore.Client;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.InternalTooling;
using EventTypeFilter = EventStore.Client.EventTypeFilter;

namespace Nvx.ConsistentAPI.TestUtils;

internal class TestConsistencyStateManager(
  EventStoreClient eventStoreClient,
  ConsistencyCheck consistencyCheck,
  ILogger logger)
{
  private const int StepDelayMilliseconds = 100;
  private const int MinimumDelayMilliseconds = MaxDelayMilliseconds / 4;
  private const int MaxDelayMilliseconds = 3_000;
  private readonly SemaphoreSlim lastEventSemaphore = new(1, 1);
  private ulong lastEventPosition;

  private int testsWaiting;

  private async Task<ulong> GetLastEventPosition()
  {
    try
    {
      await lastEventSemaphore.WaitAsync();
      await foreach (var evt in eventStoreClient.ReadAllAsync(
                       Direction.Forwards,
                       lastEventPosition == 0
                         ? Position.Start
                         : new Position(lastEventPosition, lastEventPosition),
                       EventTypeFilter.ExcludeSystemEvents()))
      {
        lastEventPosition = evt.Event.Position.CommitPosition;
      }

      return lastEventPosition;
    }
    finally
    {
      lastEventSemaphore.Release();
    }
  }

  private TimeSpan GetMinimumDelayForCheck(ConsistencyWaitType waitType)
  {
    var steps = Math.Min(1, testsWaiting);
    var typeMultiplier = waitType switch
    {
      ConsistencyWaitType.Short => 1,
      ConsistencyWaitType.Medium => 2,
      _ => 4
    };
    var delay = Math.Max(MinimumDelayMilliseconds, steps * StepDelayMilliseconds) * typeMultiplier;
    var milliseconds = Math.Min(MaxDelayMilliseconds, delay);
    return TimeSpan.FromMilliseconds(milliseconds);
  }

  private async Task WaitForAfterProcessing(ulong? position = null, int generation = 3)
  {
    if (generation == 0)
    {
      return;
    }

    while (!consistencyCheck.AfterProcessingIsDone(position ?? await GetLastEventPosition()))
    {
      await Task.Delay(10);
    }

    // ReSharper disable once TailRecursiveCall
    await WaitForAfterProcessing(await GetLastEventPosition(), generation - 1);
  }

  public async Task WaitForConsistency(int timeout, ConsistencyWaitType type)
  {
    await WaitForAfterProcessing(
      generation: type switch
      {
        ConsistencyWaitType.Short => 3,
        ConsistencyWaitType.Medium => 4,
        _ => 6
      });
    Interlocked.Increment(ref testsWaiting);
    var timer = Stopwatch.StartNew();
    var isConsistent = false;
    var lastEventForThisRun = await GetLastEventPosition();

    // Verify consistency for this check
    while (timer.ElapsedMilliseconds < timeout && !await IsConsistent(lastEventForThisRun))
    {
      await Task.Delay(Random.Shared.Next(5, 25));
    }

    // Verify full consistency
    while (timer.ElapsedMilliseconds < timeout && !(isConsistent = await IsConsistent(await GetLastEventPosition())))
    {
      await Task.Delay(Random.Shared.Next(5, 25));
    }

    Interlocked.Decrement(ref testsWaiting);

    if (!isConsistent)
    {
      // This will let go, but tests are expected to fail if consistency was not reached.
      logger.LogCritical("Timed out waiting for consistency in an integration test");
    }

    return;

    async Task<bool> IsConsistent(ulong pos) => await consistencyCheck.IsConsistentAt(pos);
  }
}
