namespace Nvx.ConsistentAPI.Store.Tests;

public class StreamTruncation
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "truncates stream")]
  [MemberData(nameof(Stores))]
  public async Task Test8(StoreBackend backend)
  {
    var eventStore = await StoreProvider.GetStore(backend);
    var swimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();
    await eventStore.Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events)).ShouldBeOk();

    await eventStore.TruncateStream(streamId, (ulong)StoreProvider.EventCount / 2);
    var readAfterTruncate = 0UL;
    var messagesAfterTruncate = eventStore.Read(ReadStreamRequest.Forwards(swimlane, streamId));
    await foreach (var msg in messagesAfterTruncate)
    {
      switch (msg)
      {
        case ReadStreamMessage<EventModelEvent>.SolvedEvent(var sl, _, _, var md):
          readAfterTruncate += 1;
          break;
      }
    }

    Assert.Equal(StoreProvider.EventCount - (ulong)StoreProvider.EventCount / 2 + 1, readAfterTruncate);
  }
}
