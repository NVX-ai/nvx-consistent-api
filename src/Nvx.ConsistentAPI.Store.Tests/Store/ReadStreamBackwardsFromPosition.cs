namespace Eventively.Tests;

public class ReadStreamBackwardsFromPosition
{
  public static TheoryData<EventStore<Event>> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read stream backwards from a specific position")]
  [MemberData(nameof(Stores))]
  public async Task Test10(EventStore<Event> eventStore)
  {
    var swimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable.Range(0, StoreProvider.EventCount).Select(Event (_) => new MyEvent(streamId)).ToArray();
    await eventStore.Insert(new InsertionPayload<Event>(swimlane, streamId, events)).ShouldBeOk();

    var messages = eventStore.Read(ReadStreamRequest.Before(swimlane, streamId, StoreProvider.EventCount / 2 + 1));
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

    Assert.Equal(StoreProvider.EventCount / 2, readFromStream);
  }
}
