using System.Diagnostics;
using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.InternalTooling;
using EventTypeFilter = KurrentDB.Client.EventTypeFilter;

namespace Nvx.ConsistentAPI.TestUtils;

internal class TestConsistencyStateManager(
  KurrentDBClient eventStoreClient,
  ConsistencyCheck consistencyCheck,
  ILogger logger)
{
  private readonly SemaphoreSlim lastEventSemaphore = new(1, 1);
  private ulong lastEventPosition;

  private async Task<ulong> GetLastEventPosition()
  {
    try
    {
      var lastEventPositionBeforeSemaphore = lastEventPosition;
      await lastEventSemaphore.WaitAsync();
      if (lastEventPositionBeforeSemaphore < lastEventPosition)
      {
        return lastEventPosition;
      }

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
        ConsistencyWaitType.Medium => 6,
        _ => 9
      });
    var timer = Stopwatch.StartNew();
    var timesConsistent = 0;
    var consistenciesNeeded = type switch
    {
      ConsistencyWaitType.Short => 1,
      ConsistencyWaitType.Medium => 3,
      _ => 5
    };

    // Verify full consistency
    while (timer.ElapsedMilliseconds < timeout)
    {
      timesConsistent = await IsConsistent(await GetLastEventPosition()) ? timesConsistent + 1 : 0;

      if (timesConsistent >= consistenciesNeeded)
      {
        break;
      }
    }

    if (timesConsistent < consistenciesNeeded)
    {
      // This will let go, but tests are expected to fail if consistency was not reached.
      logger.LogCritical("Timed out waiting for consistency in an integration test");
    }

    return;

    async Task<bool> IsConsistent(ulong pos) => await consistencyCheck.IsConsistentAt(pos);
  }
}
