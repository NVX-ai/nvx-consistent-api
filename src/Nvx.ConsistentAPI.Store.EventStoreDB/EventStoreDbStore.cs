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
        var eventData = ToEventData(payload.Insertions);
        return payload.InsertionType switch
        {
          StreamCreation => await CreateStream(eventData, streamName),
          InsertAfter(var pos) => await InsertAfter(eventData, streamName, pos),
          ExistingStream => await InExistingStream(eventData, streamName),
          _ => await AnyState(eventData, streamName)
        };
      }
      catch
      {
        return new InsertionFailure.InsertionFailed();
      }
    }
  }

  public async IAsyncEnumerable<ReadAllMessage> Read(
    ReadAllRequest request = default,
    CancellationToken cancellationToken = default)
  {
    await foreach (var msg in)
  }

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

  private async Task<Result<InsertionSuccess, InsertionFailure>> AnyState(
    IEnumerable<EventData> eventData,
    string streamName)
  {
    try
    {
      var result = await client.AppendToStreamAsync(streamName, StreamState.Any, eventData);
      // NextExpectedStreamRevision is the position of the last inserted event.
      return new InsertionSuccess(result.LogPosition.CommitPosition, result.NextExpectedStreamRevision);
    }
    catch
    {
      return new InsertionFailure.InsertionFailed();
    }
  }

  private async Task<Result<InsertionSuccess, InsertionFailure>> InsertAfter(
    IEnumerable<EventData> eventData,
    string streamName,
    ulong position)
  {
    try
    {
      var result = await client.AppendToStreamAsync(streamName, StreamRevision.FromInt64((long)position), eventData);
      // NextExpectedStreamRevision is the position of the last inserted event.
      return new InsertionSuccess(result.LogPosition.CommitPosition, result.NextExpectedStreamRevision);
    }
    catch (WrongExpectedVersionException)
    {
      return new InsertionFailure.ConsistencyCheckFailed();
    }
    catch
    {
      return new InsertionFailure.InsertionFailed();
    }
  }

  private async Task<Result<InsertionSuccess, InsertionFailure>> CreateStream(
    IEnumerable<EventData> eventData,
    string streamName)
  {
    try
    {
      var result = await client.AppendToStreamAsync(streamName, StreamState.NoStream, eventData);
      // NextExpectedStreamRevision is the position of the last inserted event.
      return new InsertionSuccess(result.LogPosition.CommitPosition, result.NextExpectedStreamRevision);
    }
    catch (WrongExpectedVersionException)
    {
      return new InsertionFailure.ConsistencyCheckFailed();
    }
    catch
    {
      return new InsertionFailure.InsertionFailed();
    }
  }

  private async Task<Result<InsertionSuccess, InsertionFailure>> InExistingStream(
    IEnumerable<EventData> eventData,
    string streamName)
  {
    try
    {
      var result = await client.AppendToStreamAsync(streamName, StreamState.StreamExists, eventData);
      // NextExpectedStreamRevision is the position of the last inserted event.
      return new InsertionSuccess(result.LogPosition.CommitPosition, result.NextExpectedStreamRevision);
    }
    catch (WrongExpectedVersionException)
    {
      return new InsertionFailure.ConsistencyCheckFailed();
    }
    catch
    {
      return new InsertionFailure.InsertionFailed();
    }
  }

  internal static IEnumerable<EventData> ToEventData(
    (EventModelEvent Event, EventInsertionMetadataPayload Metadata)[] insertions) =>
    insertions.Select(i =>
      new EventData(
        Uuid.FromGuid(i.Metadata.EventId),
        i.Event.EventType,
        i.Event.ToBytes(),
        new EventMetadata(
            i.Metadata.CreatedAt,
            i.Metadata.CorrelationId,
            i.Metadata.CausationId,
            i.Metadata.RelatedUserSub,
            null,
            null)
          .ToBytes()
      ));
}
