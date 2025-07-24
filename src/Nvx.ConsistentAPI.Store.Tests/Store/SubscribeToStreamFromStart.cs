using System.Diagnostics;

namespace Nvx.ConsistentAPI.Store.Tests;

public class SubscribeToStreamFromStart
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "subscribe to stream from start")]
  [MemberData(nameof(Stores))]
  public async Task Test14(StoreBackend backend)
  {
    var eventStore = await StoreProvider.GetStore(backend);
    var swimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();
    var eventsReceivedBySubscription = 0;
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();

    await SubscribeToStream(
      eventStore,
      SubscribeStreamRequest.FromStart(swimlane, streamId),
      message =>
      {
        switch (message)
        {
          case ReadStreamMessage<EventModelEvent>.SolvedEvent(var sl, var sid, _, _):
            Assert.Equal(swimlane, sl);
            Assert.Equal(streamId, sid);
            Interlocked.Increment(ref eventsReceivedBySubscription);
            break;
        }
      });


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
    while (!hasStarted)
    {
      await Task.Delay(5);
    }
  }
}
