using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI.TestUtils;

internal static class InstanceTracking
{
  private static readonly SemaphoreSlim Semaphore = new(1);
  private static readonly Dictionary<int, TestSetupHolder> Holders = new();

  internal static async Task<TestSetup> Get(EventModel model, TestSettings? settings = null)
  {
    var hash = model.GetHashCode();
    await Semaphore.WaitAsync();
    var testSettings = settings ?? new TestSettings();
    if (Holders.TryGetValue(hash, out var h))
    {
      Holders[hash] = h with { Count = h.Count + 1 };
      Semaphore.Release();

      await h.TestConsistencyStateManager.WaitForConsistency(
        testSettings.WaitForCatchUpTimeout,
        ConsistencyWaitType.Long);
      return new TestSetup(
        h.Url,
        h.Auth,
        h.EventStoreClient,
        h.Model,
        testSettings.WaitForCatchUpTimeout,
        h.TestConsistencyStateManager,
        h.Fetcher,
        h.Parser);
    }

    var holder = await TestSetup.InitializeInternal(model, testSettings);
    holder.Logger.LogInformation("Initialized test setup for {Hash}", hash);
    Holders[hash] = holder;
    Semaphore.Release();
    await holder.TestConsistencyStateManager.WaitForConsistency(
      testSettings.WaitForCatchUpTimeout,
      ConsistencyWaitType.Long);
    return new TestSetup(
      holder.Url,
      holder.Auth,
      holder.EventStoreClient,
      holder.Model,
      testSettings.WaitForCatchUpTimeout,
      holder.TestConsistencyStateManager,
      holder.Fetcher,
      holder.Parser);
  }

  internal static Task Dispose(int _) => Task.CompletedTask;
}
