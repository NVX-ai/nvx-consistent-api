namespace Nvx.ConsistentAPI.Store.Store;

public interface EventStore<EventInterface>
{
  Task Initialize(CancellationToken cancellationToken = default);

  AsyncResult<InsertionSuccess, InsertionFailure> Insert(InsertionPayload<EventInterface> payload);

  IAsyncEnumerable<ReadAllMessage> Read(
    ReadAllRequest request = default,
    CancellationToken cancellationToken = default);

  IAsyncEnumerable<ReadStreamMessage<EventInterface>> Read(
    ReadStreamRequest request,
    CancellationToken cancellationToken = default);

  IAsyncEnumerable<ReadAllMessage> Subscribe(
    SubscribeAllRequest request = default,
    CancellationToken cancellationToken = default);

  IAsyncEnumerable<ReadStreamMessage<EventInterface>> Subscribe(
    SubscribeStreamRequest request,
    CancellationToken cancellationToken = default);

  Task TruncateStream(string swimlane, StrongId id, ulong truncateBefore);
}
