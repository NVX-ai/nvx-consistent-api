using Nvx.ConsistentAPI.Store.Events;

namespace Nvx.ConsistentAPI.Store.Store;

public interface EventStore<EventInterface> where EventInterface : HasSwimlane, HasEntityId
{
  Task Initialize(CancellationToken cancellationToken = default);

  AsyncResult<InsertionSuccess, InsertionFailure> Insert(InsertionPayload<EventInterface> payload);

  IAsyncEnumerable<ReadAllMessage<EventInterface>> Read(
    ReadAllRequest request = default,
    CancellationToken cancellationToken = default);

  IAsyncEnumerable<ReadStreamMessage<EventInterface>> Read(
    ReadStreamRequest request,
    CancellationToken cancellationToken = default);

  IAsyncEnumerable<ReadAllMessage<EventInterface>> Subscribe(
    SubscribeAllRequest request = default,
    CancellationToken cancellationToken = default);

  IAsyncEnumerable<ReadStreamMessage<EventInterface>> Subscribe(
    SubscribeStreamRequest request,
    CancellationToken cancellationToken = default);

  Task TruncateStream(string swimlane, StrongId id, long truncateBefore);
}
