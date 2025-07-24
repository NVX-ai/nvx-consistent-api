namespace Eventively.Tests;

public class ReadAllBackwardFromPosition
{
  public static TheoryData<EventStore<Event>> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read all backwards from a specific position")]
  [MemberData(nameof(Stores))]
  public async Task Test3(EventStore<Event> eventStore)
  {
    var swimlane = Guid.NewGuid().ToString();
    var otherSwimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable.Range(0, StoreProvider.EventCount).Select(Event (_) => new MyEvent(streamId)).ToArray();
    await eventStore.Insert(new InsertionPayload<Event>(otherSwimlane, streamId, events)).ShouldBeOk();
    var insertion = await eventStore
      .Insert(new InsertionPayload<Event>(swimlane, streamId, events))
      .ShouldBeOk();
    var eventsBefore = insertion.GlobalPosition;
    var readFromAll = 0;
    var position = long.MaxValue;
    await foreach (var msg in eventStore.Read(ReadAllRequest.Before(eventsBefore, [swimlane])))
    {
      switch (msg)
      {
        case ReadAllMessage.AllEvent(var sl, _, var md):
          readFromAll += 1;
          Assert.True(position > md.GlobalPosition);
          Assert.True(md.GlobalPosition < eventsBefore);
          position = md.GlobalPosition;

          Assert.Equal(swimlane, sl);
          break;
      }
    }

    Assert.Equal(StoreProvider.EventCount - 1, readFromAll);
  }
}
