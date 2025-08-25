using System.Diagnostics;

namespace Nvx.ConsistentAPI.Store.Tests;

public class SubscribeToStreamFromNowOn
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "subscribe to stream from now on")]
  [MemberData(nameof(Stores))]
  public async Task Test16(StoreBackend backend)
  {
    await using var testStore = await StoreProvider.GetStore(backend);
    var eventStore = testStore.Store;
    const string swimlane = "MyTestSwimLane";
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();

    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();

    var eventsReceivedBySubscription = 0;

    // Do a full read so the subscription has indexes to work with.
    await foreach (var _ in eventStore.Read(ReadStreamRequest.Forwards(swimlane, streamId))) { }

    await SubscribeToStream(
      eventStore,
      SubscribeStreamRequest.FromNowOn(swimlane, streamId),
      evt =>
      {
        if (evt is ReadStreamMessage<EventModelEvent>.SolvedEvent)
        {
          Interlocked.Increment(ref eventsReceivedBySubscription);
        }
      });

    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();

    var stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < StoreProvider.SubscriptionTimeout
           && eventsReceivedBySubscription < StoreProvider.EventCount)
    {
      await Task.Delay(5);
    }

    Assert.Equal(StoreProvider.EventCount, eventsReceivedBySubscription);
  }

  private static async Task SubscribeToStream(
    EventStore<EventModelEvent> eventStore,
    SubscribeStreamRequest request,
    Action<ReadStreamMessage<EventModelEvent>> onMessage)
  {
    var stopwatch = Stopwatch.StartNew();
    var hasStarted = false;
    _ = Task.Run(async () =>
    {
      await foreach (var message in eventStore.Subscribe(request))
      {
        if (message is ReadStreamMessage<EventModelEvent>.ReadingStarted)
        {
          hasStarted = true;
          continue;
        }

        onMessage(message);
      }
    });
    while (!hasStarted && stopwatch.ElapsedMilliseconds < 2_500)
    {
      await Task.Delay(5);
    }
  }
}
