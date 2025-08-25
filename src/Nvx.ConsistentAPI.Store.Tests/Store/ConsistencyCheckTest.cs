namespace Nvx.ConsistentAPI.Store.Tests;

public class ConsistencyCheckTest
{
  public static TheoryData<StoreBackend> Stores => StoreProvider.Stores;


  [Theory(DisplayName = "insertion gives consistency error when stream position is not up to date")]
  [MemberData(nameof(Stores))]
  public async Task Test17(StoreBackend backend)
  {
    await using var testStore = await StoreProvider.GetStore(backend);
    var eventStore = testStore.Store;
    const string swimlane = "MyTestSwimLane";
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable
      .Range(0, StoreProvider.EventCount)
      .Select(EventModelEvent (_) => new MyEvent(streamId.Value))
      .ToArray();

    var result = await eventStore
      .Insert(new InsertionPayload<EventModelEvent>(swimlane, streamId, events))
      .ShouldBeOk();

    var incorrectPosition = result.StreamPosition - 5;

    await eventStore
      .Insert(
        new InsertionPayload<EventModelEvent>(
          swimlane,
          streamId,
          new InsertAfter(incorrectPosition),
          null,
          null,
          null,
          events))
      .ShouldBeError(err =>
      {
        Assert.IsType<InsertionFailure.ConsistencyCheckFailed>(err);
        return unit;
      });

    await eventStore
      .Insert(
        new InsertionPayload<EventModelEvent>(
          swimlane,
          streamId,
          new InsertAfter(result.StreamPosition),
          null,
          null,
          null,
          events))
      .ShouldBeOk();
  }
}
