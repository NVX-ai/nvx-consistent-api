using System.Diagnostics;

namespace Nvx.ConsistentAPI.Store.Tests;

public class SubscribeToAllFromStart
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "subscribe to all")]
  [MemberData(nameof(Stores))]
  public async Task Test11(StoreBackend backend)
  {
    var eventStore = await StoreProvider.GetStore(backend);
    var swimlane = Guid.NewGuid().ToString();
    var otherSwimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();
    var eventsReceivedByAllSubscription = 0;
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();

    var swimLaneStreamPosition = 0UL;
    var otherSwimLaneStreamPosition = 0UL;

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
            if (sl == swimlane || sl == otherSwimlane)
            {
              Interlocked.Increment(ref eventsReceivedByAllSubscription);
            }

            if (sl == swimlane)
            {
              if (md.StreamPosition != swimLaneStreamPosition + 1)
              {
                skippedSwimlaneStreamPositions.Add((swimLaneStreamPosition, md.StreamPosition));
              }

              swimLaneStreamPosition = md.StreamPosition;
            }

            if (sl == otherSwimlane)
            {
              if (md.StreamPosition != otherSwimLaneStreamPosition + 1)
              {
                skippedOtherSwimlaneStreamPositions.Add((otherSwimLaneStreamPosition, md.StreamPosition));
              }

              otherSwimLaneStreamPosition = md.StreamPosition;
            }

            break;
          case ReadAllMessage.ToxicAllEvent e:
            toxicEvents.Add(e);
            break;
        }
      },
      SubscribeAllRequest.Start());

    await eventStore.Insert(new InsertionPayload<EventModelEvent>(otherSwimlane, streamId, events)).ShouldBeOk();
    var stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < StoreProvider.SubscriptionTimeout
           && eventsReceivedByAllSubscription < StoreProvider.EventCount * 2)
    {
      await Task.Delay(5);
    }

    if (toxicEvents.Count != 0)
    {
      Assert.Fail($"Toxic events received: {string.Join(", ", toxicEvents)}");
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
        + $"received {eventsReceivedByAllSubscription} starting from the beginning, "
        + $"for swimlanes {swimlane} and {otherSwimlane}");
    }

    Assert.Equal(StoreProvider.EventCount * 2, eventsReceivedByAllSubscription);
  }

  private static async Task SubscribeToAll(
    EventStore<EventModelEvent> eventStore,
    Action<ReadAllMessage> onMessage,
    SubscribeAllRequest request = default)
  {
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

    while (!hasStartedReading)
    {
      await Task.Delay(5);
    }
  }
}
