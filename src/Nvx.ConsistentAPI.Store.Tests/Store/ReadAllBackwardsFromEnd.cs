namespace Nvx.ConsistentAPI.Store.Tests;

public class ReadAllBackwardsFromEnd
{
  public static TheoryData<EventStore<EventModelEvent>> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read all backwards from the end")]
  [MemberData(nameof(Stores))]
  public async Task Test1(EventStore<EventModelEvent> eventStore)
  {
    var swimlane = Guid.NewGuid().ToString();
    var otherSwimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(otherSwimlane, streamId, events)).ShouldBeOk();
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();

    var messages = eventStore.Read(ReadAllRequest.End());
    var readFromAll = 0;
    var position = ulong.MaxValue;
    var readFromStream = 0;
    await foreach (var msg in messages)
    {
      switch (msg)
      {
        case ReadAllMessage.AllEvent(var sl, _, var md):
          readFromAll += 1;
          Assert.True(position > md.GlobalPosition);
          position = md.GlobalPosition;
          readFromStream += sl == swimlane ? 1 : 0;
          break;
      }
    }

    Assert.Equal(StoreProvider.EventCount, readFromStream);
    Assert.True(StoreProvider.EventCount <= readFromAll);
  }
}
