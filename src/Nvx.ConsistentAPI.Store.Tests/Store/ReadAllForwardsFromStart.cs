namespace Nvx.ConsistentAPI.Store.Tests;

public class ReadAllForwardsFromStart
{
  public static TheoryData<EventStore<EventModelEvent>> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read all forwards from the start")]
  [MemberData(nameof(Stores))]
  public async Task Test0(EventStore<EventModelEvent> eventStore)
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

    var messages = eventStore.Read(ReadAllRequest.Start());
    var readFromAll = 0;
    var position = ulong.MinValue;
    var readFromStream = 0;
    await foreach (var msg in messages)
    {
      switch (msg)
      {
        case ReadAllMessage.AllEvent(var sl, _, var md):
          readFromAll += 1;
          Assert.True(position < md.GlobalPosition);
          position = md.GlobalPosition;
          readFromStream += sl == swimlane ? 1 : 0;
          break;
      }

      if (readFromStream >= StoreProvider.EventCount)
      {
        break;
      }
    }


    Assert.Equal(StoreProvider.EventCount, readFromStream);
    Assert.True(StoreProvider.EventCount <= readFromAll);
  }
}
