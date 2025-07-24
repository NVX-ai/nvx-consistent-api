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

  private static readonly Lazy<EventStore<EventModelEvent>> EsDBStore = new(() =>
  {
    var container = new EventStoreDbBuilder()
      .WithImage(EventStoreDefaultImage)
      .WithEnvironment("EVENTSTORE_MEM_DB", "True")
      .Build();
    container.StartAsync().Wait();
    var store = new EventStoreDbStore();
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

    throw new TimeoutException("Failed to initialize MsSqlEventStore within 1 minute.");
  });

  public static TheoryData<EventStore<EventModelEvent>> Stores =>
  [
    EsDBStore.Value
  ];
}

public record MyEventId(Guid Value) : StrongId
{
  public const string Swimlane = "MyEvent";
  public string Inlined => Value.ToString();
  public override string StreamId() => $"{Swimlane}{Value}";
  public override string ToString() => StreamId();
}

public record MyEvent(MyEventId StreamId) : Event<MyEventId>
{
  public MyEventId Id => StreamId;
}
