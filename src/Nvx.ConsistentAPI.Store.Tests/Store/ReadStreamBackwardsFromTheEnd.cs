namespace Eventively.Tests;

public class ReadStreamBackwardsFromTheEnd
{
  public static TheoryData<EventStore<Event>> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read stream backwards from the end")]
  [MemberData(nameof(Stores))]
  public async Task Test7(EventStore<Event> eventStore)
  {
    var swimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable.Range(0, StoreProvider.EventCount).Select(Event (_) => new MyEvent(streamId)).ToArray();
    await eventStore.Insert(new InsertionPayload<Event>(swimlane, streamId, events)).ShouldBeOk();

    var messages = eventStore.Read(ReadStreamRequest.Backwards(swimlane, streamId));
    var readFromStream = 0;
    var position = long.MaxValue;
    await foreach (var msg in messages)
    {
      switch (msg)
      {
        case ReadStreamMessage<Event>.SolvedEvent(var sl, _, _, var md):
          readFromStream += 1;
          Assert.True(position > md.GlobalPosition);
          position = md.GlobalPosition;
          Assert.Equal(swimlane, sl);
          break;
      }
    }

    Assert.Equal(StoreProvider.EventCount, readFromStream);
  }
}
