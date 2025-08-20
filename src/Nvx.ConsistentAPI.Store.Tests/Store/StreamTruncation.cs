namespace Nvx.ConsistentAPI.Store.Tests;

public class StreamTruncation
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "truncates stream")]
  [MemberData(nameof(Stores))]
  public async Task Test8(StoreBackend backend)
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

    await eventStore.TruncateStream(swimlane, streamId, (long)StoreProvider.EventCount / 2);
    var readAfterTruncate = 0UL;
    var messagesAfterTruncate = eventStore.Read(ReadStreamRequest.Forwards(swimlane, streamId));
    await foreach (var msg in messagesAfterTruncate)
    {
      switch (msg)
      {
        case ReadStreamMessage<EventModelEvent>.SolvedEvent:
          readAfterTruncate += 1;
          break;
      }
    }

    // No, it's not just EventCount/2, because dividing an integer by two rounds down, and this rounds up.
    Assert.Equal((ulong) (StoreProvider.EventCount - StoreProvider.EventCount / 2), readAfterTruncate);
  }
}
