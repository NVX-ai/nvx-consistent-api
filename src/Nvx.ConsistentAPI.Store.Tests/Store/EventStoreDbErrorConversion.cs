using Nvx.ConsistentAPI.Store.EventStoreDB;

namespace Nvx.ConsistentAPI.Store.Tests;

public class EventStoreDbErrorConversion
{
  [Fact(DisplayName = "should convert to read all message")]
  public void Test1() => Assert.NotNull(
    EventStoreDbStore<EventModelEvent>.CreateTerminatedMessage<ReadAllMessage<EventModelEvent>>(new Exception()));

  [Fact(DisplayName = "should convert to read stream message")]
  public void Test2() => Assert.NotNull(
    EventStoreDbStore<EventModelEvent>.CreateTerminatedMessage<ReadStreamMessage<EventModelEvent>>(new Exception()));
}
