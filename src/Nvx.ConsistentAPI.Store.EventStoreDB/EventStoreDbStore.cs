using DeFuncto;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI.Store.EventStoreDB;

public class EventStoreDbStore(string connectionString) : EventStore<EventModelEvent>
{
  public Task Initialize(CancellationToken cancellationToken = default) => throw new NotImplementedException();

  public AsyncResult<InsertionSuccess, InsertionFailure> Insert(InsertionPayload<EventModelEvent> payload) =>
    throw new NotImplementedException();

  public IAsyncEnumerable<ReadAllMessage> Read(
    ReadAllRequest request = default,
    CancellationToken cancellationToken = default) => throw new NotImplementedException();

  public IAsyncEnumerable<ReadStreamMessage<EventModelEvent>> Read(
    ReadStreamRequest request,
    CancellationToken cancellationToken = default) => throw new NotImplementedException();

  public IAsyncEnumerable<ReadAllMessage> Subscribe(
    SubscribeAllRequest request = default,
    CancellationToken cancellationToken = default) => throw new NotImplementedException();

  public IAsyncEnumerable<ReadStreamMessage<EventModelEvent>> Subscribe(
    SubscribeStreamRequest request,
    CancellationToken cancellationToken = default) => throw new NotImplementedException();

  public Task TruncateStream(StrongId id, ulong truncateBefore) => throw new NotImplementedException();
}
