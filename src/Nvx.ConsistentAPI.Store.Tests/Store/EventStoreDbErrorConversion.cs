using Nvx.ConsistentAPI.Store.EventStoreDB;

namespace Nvx.ConsistentAPI.Store.Tests;

public class EventStoreDbErrorConversion
{
  [Fact(DisplayName = "should convert to read all message")]
  public void Test1()
  {
    var error = EventStoreDbStore.CreateTerminatedMessage<ReadAllMessage<EventModelEvent>>(new Exception());
    Assert.NotNull(error);
  }

  [Fact(DisplayName = "should convert to read stream message")]
  public void Test2()
  {
    var error = EventStoreDbStore.CreateTerminatedMessage<ReadStreamMessage<EventModelEvent>>(new Exception());
    Assert.NotNull(error);
  }
}
