namespace Eventively.Tests;

public class ReadAllForwardsFromPosition
{
  public static TheoryData<EventStore<Event>> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read all forwards from a specific position")]
  [MemberData(nameof(Stores))]
  public async Task Test2(EventStore<Event> eventStore)
  {
    var swimlane = Guid.NewGuid().ToString();
    var otherSwimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());
    const int skipCount = 10;
    var events = Enumerable.Range(0, StoreProvider.EventCount).Select(Event (_) => new MyEvent(streamId)).ToArray();
    await eventStore.Insert(new InsertionPayload<Event>(swimlane, streamId, events)).ShouldBeOk();
    await eventStore.Insert(new InsertionPayload<Event>(otherSwimlane, streamId, events)).ShouldBeOk();
    var eventCount = 0L;
    var tenthEventPosition = 0L;
    await foreach (var msg in eventStore.Read(ReadStreamRequest.Forwards(swimlane, streamId)))
    {
      switch (msg)
      {
        case ReadStreamMessage<Event>.SolvedEvent(_, _, _, var md):
          eventCount += 1;
          if (eventCount == skipCount)
          {
            tenthEventPosition = md.GlobalPosition;
          }

          break;
      }
    }

    var readFromAll = 0;
    var position = long.MinValue;

    await foreach (var msg in eventStore.Read(ReadAllRequest.After(tenthEventPosition, [swimlane])))
    {
      switch (msg)
      {
        case ReadAllMessage.AllEvent(var sl, _, var md):
          readFromAll += 1;
          Assert.True(position < md.GlobalPosition);
          position = md.GlobalPosition;
          Assert.Equal(swimlane, sl);
          break;
      }
    }

    // Streams start at 1
    Assert.Equal(StoreProvider.EventCount - skipCount, readFromAll);
  }
}
