using DeFuncto.Assertions;
using Nvx.ConsistentAPI;
using Nvx.ConsistentAPI.Store.Store;

namespace Eventively.Tests;

public class ConsistencyCheckTest
{
  public static TheoryData<EventStore<EventModelEvent>> Stores => StoreProvider.Stores;


  [Theory(DisplayName = "insertion gives consistency error when stream position is not up to date")]
  [MemberData(nameof(Stores))]
  public async Task Test17(EventStore<Event> eventStore)
  {
    var swimlane = Guid.NewGuid().ToString();
    var streamId = new MyEventId(Guid.NewGuid());
    var events = Enumerable.Range(0, StoreProvider.EventCount).Select(Event (_) => new MyEvent(streamId)).ToArray();

    var result = await eventStore
      .Insert(new InsertionPayload<Event>(swimlane, streamId, events))
      .ShouldBeOk();

    var incorrectPosition = result.StreamPosition - 5;

    await eventStore
      .Insert(new InsertionPayload<Event>(swimlane, streamId, incorrectPosition, null, null, null, events))
      .ShouldBeError(err =>
      {
        Assert.IsType<InsertionFailure.ConsistencyCheckFailed>(err);
        return unit;
      });

    await eventStore
      .Insert(new InsertionPayload<Event>(swimlane, streamId, result.StreamPosition, null, null, null, events))
      .ShouldBeOk();
  }
}
