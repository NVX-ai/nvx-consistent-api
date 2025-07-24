using System.Diagnostics;
using System.Runtime.InteropServices;
using Nvx.ConsistentAPI.Store.EventStoreDB;
using Nvx.ConsistentAPI.Store.Store;
using Testcontainers.EventStoreDb;

namespace Nvx.ConsistentAPI.Store.Tests;

public static class StoreProvider
{
  public const int EventCount = 418;

  private static string EventStoreDefaultImage =>
    RuntimeInformation.ProcessArchitecture == Architecture.Arm64
    && RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
      ? "eventstore/eventstore:23.10.0-alpha-arm64v8"
      : "eventstore/eventstore:23.10.0-jammy";

  public static readonly TimeSpan SubscriptionTimeout = TimeSpan.FromSeconds(10);

  private static readonly Lazy<EventStore<EventModelEvent>> EsDbStore = new(() =>
  {
    var container = new EventStoreDbBuilder()
      .WithImage(EventStoreDefaultImage)
      .WithEnvironment("EVENTSTORE_MEM_DB", "True")
      .Build();
    container.StartAsync().Wait();
    var store = new EventStoreDbStore(container.GetConnectionString());
    var stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < TimeSpan.FromMinutes(1))
    {
      try
      {
        store.Initialize().Wait();
        return store;
      }
      catch
      {
        // Ignore
      }
    }

    throw new TimeoutException("Failed to initialize EventStoreDbEventStore within 1 minute.");
  });

  public static TheoryData<EventStore<EventModelEvent>> Stores =>
  [
    EsDbStore.Value
  ];
}

public record MyEventId(Guid Value) : StrongId
{
  public override string SwimLane => "MyTestSwimLane";
  public override string StreamId() => Value.ToString();
  public override string ToString() => StreamId();
}

public record MyEvent(Guid Id) : EventModelEvent
{
  public StrongId GetEntityId() => new MyEventId(Id);
}
