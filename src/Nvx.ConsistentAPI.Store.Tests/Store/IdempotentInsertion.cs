namespace Nvx.ConsistentAPI.Store.Tests;

public class IdempotentInsertion
{
  public static TheoryData<EventStore<EventModelEvent>> Stores => StoreProvider.Stores;


  [Theory(DisplayName = "insertion is idempotent by event id")]
  [MemberData(nameof(Stores))]
  public async Task Test18(EventStore<EventModelEvent> eventStore)
  {
    var swimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());

    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();

    var insertionPayload = new InsertionPayload<EventModelEvent>(swimlane, streamId, events);
    await eventStore.Insert(insertionPayload).ShouldBeOk();
    await eventStore.Insert(insertionPayload).ShouldBeOk();

    var readFromStream = 0;
    await foreach (var msg in eventStore.Read(ReadStreamRequest.Forwards(swimlane, streamId)))
    {
      readFromStream = msg switch
      {
        ReadStreamMessage<EventModelEvent>.SolvedEvent => readFromStream + 1,
        _ => readFromStream
      };
    }

    Assert.Equal(StoreProvider.EventCount, readFromStream);
  }
}
