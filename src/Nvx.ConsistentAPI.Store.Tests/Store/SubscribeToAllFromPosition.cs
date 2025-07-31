using System.Diagnostics;

namespace Nvx.ConsistentAPI.Store.Tests;

public class SubscribeToAllFromPosition
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "subscribe to all from specific position")]
  [MemberData(nameof(Stores))]
  public async Task Test13(StoreBackend backend)
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
      .Select(EventModelEvent (_) => new MyOtherEvent(otherStreamId.Value))
      .ToArray();

    var firstInsertion =
      await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();

    var positionAfterFirstBatch = firstInsertion.GlobalPosition;
    var eventsReceivedBySubscription = 0;

    var swimLaneStreamPosition = firstInsertion.StreamPosition;
    ulong? otherSwimLaneStreamPosition = null;

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
              Interlocked.Increment(ref eventsReceivedBySubscription);
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
              if (otherSwimLaneStreamPosition.HasValue && md.StreamPosition != otherSwimLaneStreamPosition + 1)
              {
                skippedOtherSwimlaneStreamPositions.Add((otherSwimLaneStreamPosition.Value, md.StreamPosition));
              }

              otherSwimLaneStreamPosition = md.StreamPosition;
            }

            break;

          case ReadAllMessage.ToxicAllEvent e:
            toxicEvents.Add(e);
            break;
        }
      },
      SubscribeAllRequest.After(positionAfterFirstBatch));

    await eventStore
      .Insert(new InsertionPayload<EventModelEvent>(otherSwimlane, otherStreamId, otherEvents))
      .ShouldBeOk();

    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();

    var stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < StoreProvider.SubscriptionTimeout
           && eventsReceivedBySubscription < StoreProvider.EventCount * 2)
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

    if (eventsReceivedBySubscription < StoreProvider.EventCount * 2)
    {
      Assert.Fail(
        $"Failed to receive all events. Expected {StoreProvider.EventCount * 2}, "
        + $"received {eventsReceivedBySubscription} starting from specific position, "
        + $"after insertion {firstInsertion}, "
        + $"for swimlanes {swimlane} and {otherSwimlane}.");
    }

    Assert.Equal(StoreProvider.EventCount * 2, eventsReceivedBySubscription);
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
