namespace Nvx.ConsistentAPI.Store.Store;

public static class ReadEventsExtensions
{
  public static IAsyncEnumerable<ReadAllMessage<EventInterface>.AllEvent> Events<EventInterface>(
    this IAsyncEnumerable<ReadAllMessage<EventInterface>> self) =>
    from msg in self
    let evt = msg as ReadAllMessage<EventInterface>.AllEvent
    where evt is not null
    select evt;

  public static IAsyncEnumerable<ReadStreamMessage<EventInterface>.SolvedEvent> Events<EventInterface>(
    this IAsyncEnumerable<ReadStreamMessage<EventInterface>> self) =>
    from msg in self
    let evt = msg as ReadStreamMessage<EventInterface>.SolvedEvent
    where evt is not null
    select evt;
}
