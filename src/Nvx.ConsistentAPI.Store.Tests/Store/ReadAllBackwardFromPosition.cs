namespace Nvx.ConsistentAPI.Store.Tests;

public class ReadAllBackwardFromPosition
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read all backwards from a specific position")]
  [MemberData(nameof(Stores))]
  public async Task Test3(StoreBackend backend)
  {
    var eventStore = await StoreProvider.GetStore(backend);
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
    var insertion = await eventStore
      .Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events))
      .ShouldBeOk();
    var eventsBefore = insertion.GlobalPosition;
    var readFromAll = 0;
    var position = ulong.MaxValue;
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
