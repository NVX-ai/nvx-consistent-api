using System.Diagnostics;

namespace Nvx.ConsistentAPI.Store.Tests;

public class SubscribeToAllFromNowOn
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "subscribe to all from now on")]
  [MemberData(nameof(Stores))]
  public async Task Test12(StoreBackend backend)
  {
    var eventStore = await StoreProvider.GetStore(backend);
    const string swimlane = "MyTestSwimLane";
    const string otherSwimlane = "MyOtherTestSwimLane";
    var streamId = new MyEventId(Guid.NewGuid());
    var otherStreamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();
    var otherEvents = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(otherStreamId.Value))
      .ToArray();
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();
    var insertion = await eventStore
      .Insert(new InsertionPayload<EventModelEvent>(otherSwimlane, otherStreamId, otherEvents))
      .ShouldBeOk();
    var eventsReceivedByAllSubscription = 0;
    ulong swimLaneStreamPosition = StoreProvider.EventCount;
    ulong otherSwimLaneStreamPosition = StoreProvider.EventCount;

    List<(ulong, ulong)> skippedSwimlaneStreamPositions = [];
    List<(ulong, ulong)> skippedOtherSwimlaneStreamPositions = [];

    List<ReadAllMessage.ToxicAllEvent> toxicEvents = [];

    await SubscribeToAll(
      eventStore,
      message =>
      {
        switch (message)
        {
          case ReadAllMessage.AllEvent(var sl, _, var md):
            if (sl is swimlane or otherSwimlane)
            {
              Interlocked.Increment(ref eventsReceivedByAllSubscription);
            }

            switch (sl)
            {
              case swimlane:
              {
                if (md.StreamPosition != swimLaneStreamPosition + 1)
                {
                  skippedSwimlaneStreamPositions.Add((swimLaneStreamPosition, md.StreamPosition));
                }

                swimLaneStreamPosition = md.StreamPosition;
                break;
              }
              case otherSwimlane:
              {
                if (md.StreamPosition != otherSwimLaneStreamPosition + 1)
                {
                  skippedOtherSwimlaneStreamPositions.Add((otherSwimLaneStreamPosition, md.StreamPosition));
                }

                otherSwimLaneStreamPosition = md.StreamPosition;
                break;
              }
            }

            break;
          case ReadAllMessage.ToxicAllEvent e:
            toxicEvents.Add(e);
            break;
        }
      },
      SubscribeAllRequest.FromNowOn());

    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(otherSwimlane, otherStreamId, otherEvents)).ShouldBeOk();

    var stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < StoreProvider.SubscriptionTimeout
           && eventsReceivedByAllSubscription < StoreProvider.EventCount * 2)
    {
      await Task.Delay(5);
    }

    if (toxicEvents.Count != 0)
    {
      Assert.Fail(
        $"Toxic events received: {string.Join(", ", toxicEvents)}");
    }

    if (skippedSwimlaneStreamPositions.Count != 0)
    {
      Assert.Fail(
        $"Skipped stream positions for swimlane {swimlane}: {string.Join(", ", skippedSwimlaneStreamPositions)}");
    }

    if (skippedOtherSwimlaneStreamPositions.Count != 0)
    {
      Assert.Fail(
        $"Skipped stream positions for swimlane {otherSwimlane}: {string.Join(", ", skippedOtherSwimlaneStreamPositions)}");
    }

    if (eventsReceivedByAllSubscription < StoreProvider.EventCount * 2)
    {
      Assert.Fail(
        $"Failed to receive all events. Expected {StoreProvider.EventCount * 2}, "
        + $"received {eventsReceivedByAllSubscription} starting now on, "
        + $"after insertion {insertion}, "
        + $"for swimlanes {swimlane} and {otherSwimlane}");
    }

    Assert.Equal(StoreProvider.EventCount * 2, eventsReceivedByAllSubscription);
  }

  private static async Task SubscribeToAll(
    EventStore<EventModelEvent> eventStore,
    Action<ReadAllMessage> onMessage,
    SubscribeAllRequest request = default)
  {
    var stopwatch = Stopwatch.StartNew();
    var hasStartedReading = false;

    _ = Task.Run(async () =>
    {
      await foreach (var message in eventStore.Subscribe(request))
      {
        if (message is ReadAllMessage.ReadingStarted)
        {
          hasStartedReading = true;
        }

        onMessage(message);
      }
    });

    while (!hasStartedReading && stopwatch.ElapsedMilliseconds < 2_500)
    {
      await Task.Delay(5);
    }
  }
}
