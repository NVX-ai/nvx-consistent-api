using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DeFuncto;
using DeFuncto.Extensions;
using Newtonsoft.Json;
using Nvx.ConsistentAPI.Store.EventStoreDB;
using Nvx.ConsistentAPI.Store.InMemory;
using Nvx.ConsistentAPI.Store.MsSql;
using Testcontainers.EventStoreDb;
using Testcontainers.MsSql;

namespace Nvx.ConsistentAPI.Store.Tests;

public enum StoreBackend
{
  InMemory,
  EventStoreDb,
  MsSql
}

public static class StoreProvider
{
  public static readonly int EventCount = Random.Shared.Next(15, 45);

  public static readonly TimeSpan SubscriptionTimeout = TimeSpan.FromSeconds(10);

  private static readonly Type[] EventTypes =
    AppDomain
      .CurrentDomain.GetAssemblies()
      .SelectMany(a => a.GetTypes())
      .Where(t => t is { IsClass: true, IsAbstract: false })
      .Where(t => t.GetInterfaces().Any(i => i == typeof(EventModelEvent)))
      .ToArray();

  private static string MsSqlDefaultImage => "mcr.microsoft.com/mssql/server:2022-latest";

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
      StoreBackend.MsSql => await MsSqlStore(),
      _ => new InMemoryEventStore<EventModelEvent>()
    };

  private static async Task<EventStore<EventModelEvent>> MsSqlStore()
  {
    var container = new MsSqlBuilder().WithImage(MsSqlDefaultImage).Build();
    await container.StartAsync();
    var store = new MsSqlEventStore<EventModelEvent>(
      container.GetConnectionString(),
      Deserialize,
      Serialize);
    await store.Initialize();
    return store;
  }

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

  private static Option<(EventModelEvent evt, StrongId streamId)> Deserialize(string eventType, byte[] bytes)
  {
    try
    {
      return EventTypes
        .FirstOrNone(t => t.Name == eventType)
        .Bind(t => Optional(JsonConvert.DeserializeObject(Encoding.UTF8.GetString(bytes), t) as EventModelEvent))
        .Map(e => (e, e.GetEntityId()));
    }
    catch
    {
      return None;
    }
  }

  private static (string typeName, byte[] data) Serialize(EventModelEvent evt) => (evt.GetType().Name,
    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(evt)));
}

public record MyEventId(Guid Value) : StrongId
{
  public override string StreamId() => Value.ToString();
  public override string ToString() => StreamId();
}

public record MyEvent(Guid Id) : EventModelEvent
{
  public string GetSwimLane() => "MyTestSwimLane";
  public StrongId GetEntityId() => new MyEventId(Id);
}

public record MyOtherEvent(Guid Id) : EventModelEvent
{
  public string GetSwimLane() => "MyOtherTestSwimLane";
  public StrongId GetEntityId() => new MyEventId(Id);
}
