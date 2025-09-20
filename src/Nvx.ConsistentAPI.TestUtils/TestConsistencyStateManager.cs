using System.Diagnostics;
using EventStore.Client;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.InternalTooling;
using EventTypeFilter = EventStore.Client.EventTypeFilter;

namespace Nvx.ConsistentAPI.TestUtils;

internal class TestConsistencyStateManager
{
  private const int StepDelayMilliseconds = 100;
  private const int MinimumDelayMilliseconds = MaxDelayMilliseconds / 4;
  private const int MaxDelayMilliseconds = 3_000;
  private readonly ConsistencyCheck consistencyCheck;
  private readonly EventStoreClient eventStoreClient;
  private readonly ILogger logger;
  private DateTime lastEventAt;
  private ulong lastEventPosition;

  private int testsWaiting;

  public TestConsistencyStateManager(
    EventStoreClient eventStoreClient,
    ConsistencyCheck consistencyCheck,
    ILogger logger)
  {
    this.eventStoreClient = eventStoreClient;
    this.consistencyCheck = consistencyCheck;
    this.logger = logger;
    Subscribe();
  }

  private void Subscribe() =>
    _ = Task.Run(async () =>
    {
      while (true)
      {
        try
        {
          await foreach (var evt in eventStoreClient.SubscribeToAll(
                           lastEventPosition == 0
                             ? FromAll.End
                             : FromAll.After(new Position(lastEventPosition, lastEventPosition)),
                           filterOptions: new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents())))
          {
            lastEventPosition = evt.Event.Position.CommitPosition;
            lastEventAt = evt.Event.Created;
          }
        }
        catch
        {
          // ignore
        }
      }
    });


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

  public async Task WaitForAfterProcessing(ulong? position = null)
  {
    while (!consistencyCheck.AfterProcessingIsDone(position ?? lastEventPosition))
    {
      await Task.Delay(10);
    }
  }

  public async Task WaitForConsistency(int timeout, ConsistencyWaitType type)
  {
    await WaitForAfterProcessing(lastEventPosition);
    Interlocked.Increment(ref testsWaiting);
    var timer = Stopwatch.StartNew();
    var isConsistent = false;
    var lastEventForThisRun = lastEventPosition;

    // Verify consistency for this check
    while (timer.ElapsedMilliseconds < timeout && !await IsConsistent(lastEventForThisRun))
    {
      await Task.Delay(Random.Shared.Next(5, 25));
    }

    // Wait for the minimum delay
    if (DateTime.UtcNow - lastEventAt < GetMinimumDelayForCheck(type))
    {
      await Task.Delay(GetMinimumDelayForCheck(type));
    }

    // Verify full consistency
    while (timer.ElapsedMilliseconds < timeout && !(isConsistent = await IsConsistent(lastEventPosition)))
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
