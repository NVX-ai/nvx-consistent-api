namespace Nvx.ConsistentAPI.Store.Tests;

public class ReadStreamBackwardsFromTheEnd
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read stream backwards from the end")]
  [MemberData(nameof(Stores))]
  public async Task Test7(StoreBackend backend)
  {
    await using var testStore = await StoreProvider.GetStore(backend);
    var eventStore = testStore.Store;
    const string swimlane = "MyTestSwimLane";
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();

    var messages = eventStore.Read(ReadStreamRequest.Backwards(swimlane, streamId));
    var readFromStream = 0;
    var position = ulong.MaxValue;
    await foreach (var msg in messages)
    {
      switch (msg)
      {
        case ReadStreamMessage<EventModelEvent>.SolvedEvent(var sl, _, _, var md):
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
