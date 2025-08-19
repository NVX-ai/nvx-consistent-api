namespace Nvx.ConsistentAPI.Store.Tests;

public class ReadAllBackwardsFromEnd
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read all backwards from the end")]
  [MemberData(nameof(Stores))]
  public async Task Test1(StoreBackend backend)
  {
    await using var testStore = await StoreProvider.GetStore(backend);
    var eventStore = testStore.Store;
    const string swimlane = "MyTestSwimLane";
    const string otherSwimlane = "MyOtherTestSwimLane";
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();

    var otherEvents = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyOtherEvent(Guid.NewGuid()))
      .ToArray();
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(otherSwimlane, streamId, otherEvents)).ShouldBeOk();
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();

    var messages = eventStore.Read(ReadAllRequest.End());
    var readFromAll = 0;
    var position = ulong.MaxValue;
    var readFromStream = 0;
    await foreach (var msg in messages)
    {
      switch (msg)
      {
        case ReadAllMessage<EventModelEvent>.AllEvent(var sl, _, _, var md):
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
