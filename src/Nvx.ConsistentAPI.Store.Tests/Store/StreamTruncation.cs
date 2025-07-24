namespace Nvx.ConsistentAPI.Store.Tests;

public class StreamTruncation
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;

  [Theory(DisplayName = "read stream forwards from the start")]
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

    var messages = eventStore.Read(ReadStreamRequest.Forwards(swimlane, streamId));
    var readFromStream = 0;
    var position = ulong.MinValue;
    await foreach (var msg in messages)
    {
      switch (msg)
      {
        case ReadStreamMessage<EventModelEvent>.SolvedEvent(var sl, _, _, var md):
          readFromStream += 1;
          Assert.True(position < md.GlobalPosition);
          position = md.GlobalPosition;
          Assert.Equal(swimlane, sl);
          break;
      }
    }

    Assert.Equal(StoreProvider.EventCount, readFromStream);

    var truncateAt = (ulong)readFromStream / 2;
    await eventStore.TruncateStream(streamId, truncateAt);
    var readAfterTruncate = 0UL;
    var messagesAfterTruncate = eventStore.Read(ReadStreamRequest.Forwards(swimlane, streamId));
    await foreach (var msg in messagesAfterTruncate)
    {
      switch (msg)
      {
        case ReadStreamMessage<EventModelEvent>.SolvedEvent(var sl, _, _, var md):
          readAfterTruncate += 1;
          Assert.True(position < md.GlobalPosition);
          position = md.GlobalPosition;
          Assert.Equal(swimlane, sl);
          break;
      }
    }

    Assert.Equal(StoreProvider.EventCount - truncateAt, readAfterTruncate);
  }
}
