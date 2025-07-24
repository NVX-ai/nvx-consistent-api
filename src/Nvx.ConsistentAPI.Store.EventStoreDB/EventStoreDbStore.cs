using DeFuncto;
using EventStore.Client;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI.Store.EventStoreDB;

public class EventStoreDbStore(string connectionString) : EventStore<EventModelEvent>
{
  private readonly EventStoreClient client = new(EventStoreClientSettings.Create(connectionString));

  public Task Initialize(CancellationToken cancellationToken = default) => Task.CompletedTask;

  public AsyncResult<InsertionSuccess, InsertionFailure> Insert(InsertionPayload<EventModelEvent> payload)
  {
    return Go();

    async Task<Result<InsertionSuccess, InsertionFailure>> Go()
    {
      try
      {
        var streamName = $"{payload.Swimlane}{payload.StreamId.StreamId()}";
        throw new NotImplementedException();
      }
      catch
      {
        return new InsertionFailure.InsertionFailed();
      }
    }
  }

  // internal static IEnumerable<EventData> ToEventData(IEnumerable<EventModelEvent> events, EventContext? context) =>
  //   events.Select(e =>
  //     new EventData(
  //       Uuid.NewUuid(),
  //       e.EventType,
  //       e.ToBytes(),
  //       new EventMetadata(
  //           DateTime.UtcNow,
  //           context?.CorrelationId,
  //           context?.CausationId,
  //           context?.RelatedUserSub,
  //           null)
  //         .ToBytes()
  //     ));

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
