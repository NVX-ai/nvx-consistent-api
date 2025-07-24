using System.Diagnostics;

namespace Eventively.Tests;

public class SubscribeToStreamFromStart
{
  public static TheoryData<EventStore<Event>> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "subscribe to stream from start")]
  [MemberData(nameof(Stores))]
  public async Task Test14(EventStore<Event> eventStore)
  {
    var swimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable.Range(0, StoreProvider.EventCount).Select(Event (_) => new MyEvent(streamId)).ToArray();
    var eventsReceivedBySubscription = 0;
    await eventStore.Insert(new InsertionPayload<Event>(swimlane, streamId, events)).ShouldBeOk();

    await SubscribeToStream(
      eventStore,
      SubscribeStreamRequest.FromStart(swimlane, streamId),
      message =>
      {
        switch (message)
        {
          case ReadStreamMessage<Event>.SolvedEvent(var sl, var sid, _, _):
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
    EventStore<Event> eventStore,
    SubscribeStreamRequest request,
    Action<ReadStreamMessage<Event>> onMessage)
  {
    var hasStarted = false;
    _ = Task.Run(async () =>
    {
      await foreach (var message in eventStore.Subscribe(request))
      {
        if (message is ReadStreamMessage<Event>.ReadingStarted)
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
