using System.Diagnostics;
using System.Runtime.InteropServices;
using Nvx.ConsistentAPI.Store.EventStoreDB;
using Nvx.ConsistentAPI.Store.InMemory;
using Testcontainers.EventStoreDb;

namespace Nvx.ConsistentAPI.Store.Tests;

public enum StoreBackend
{
  InMemory,
  EventStoreDb
}

public static class StoreProvider
{
  public static readonly int EventCount = Random.Shared.Next(15, 45);

  public static readonly TimeSpan SubscriptionTimeout = TimeSpan.FromSeconds(10);


  private static string EventStoreDefaultImage =>
    RuntimeInformation.ProcessArchitecture == Architecture.Arm64
    && RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
      ? "eventstore/eventstore:23.10.0-alpha-arm64v8"
      : "eventstore/eventstore:23.10.0-jammy";

  public static TheoryData<StoreBackend> Stores => [..Enum.GetValues<StoreBackend>()];

  public static async Task<EventStore<EventModelEvent>> GetStore(StoreBackend backend) =>
    backend switch
    {
      StoreBackend.EventStoreDb => await EsDbStore(),
      _ => new InMemoryEventStore<EventModelEvent>()
    };

  private static async Task<EventStore<EventModelEvent>> EsDbStore()
  {
    var container = new EventStoreDbBuilder()
      .WithImage(EventStoreDefaultImage)
      .WithEnvironment("EVENTSTORE_MEM_DB", "True")
      .WithEnvironment("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", "true")
      .Build();
    await container.StartAsync();

    var store = new EventStoreDbStore(container.GetConnectionString());
    var stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < TimeSpan.FromMinutes(1))
    {
      try
      {
        await store.Initialize();
        return store;
      }
      catch
      {
        // Ignore
      }
    }

    throw new TimeoutException("Failed to initialize EventStoreDbEventStore within 1 minute.");
  }
}

public record MyEventId(Guid Value) : StrongId
{
  public override string StreamId() => Value.ToString();
  public override string ToString() => StreamId();
}

public record MyEvent(Guid Id) : EventModelEvent
{
  public string SwimLane => "MyTestSwimLane";
  public StrongId GetEntityId() => new MyEventId(Id);
}

public record MyOtherEvent(Guid Id) : EventModelEvent
{
  public string SwimLane => "MyOtherTestSwimLane";
  public StrongId GetEntityId() => new MyEventId(Id);
}
