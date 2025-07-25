namespace Nvx.ConsistentAPI.Store.Tests;

public class ReadAllForwardsFromPosition
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read all forwards from a specific position")]
  [MemberData(nameof(Stores))]
  public async Task Test2(StoreBackend backend)
  {
    var eventStore = await StoreProvider.GetStore(backend);
    const string swimlane = "MyTestSwimLane";
    const string otherSwimlane = "MyOtherTestSwimLane";
    var streamId = new MyEventId(Guid.NewGuid());
    const int skipCount = 10;
    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(otherSwimlane, streamId, events)).ShouldBeOk();
    var eventCount = 0L;
    var tenthEventPosition = 0UL;
    await foreach (var msg in eventStore.Read(ReadStreamRequest.Forwards(swimlane, streamId)))
    {
      switch (msg)
      {
        case ReadStreamMessage<EventModelEvent>.SolvedEvent(_, _, _, var md):
          eventCount += 1;
          if (eventCount == skipCount)
          {
            tenthEventPosition = md.GlobalPosition;
          }

          break;
      }
    }

    var readFromAll = 0;
    var position = ulong.MinValue;

    await foreach (var msg in eventStore.Read(ReadAllRequest.FromAndAfter(tenthEventPosition, [swimlane])))
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
