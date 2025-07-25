using System.Runtime.CompilerServices;
using EventStore.Client;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI.Store.EventStoreDB;

public class EventStoreDbStore(string connectionString, Func<ResolvedEvent, Option<EventModelEvent>> parser)
  : EventStore<EventModelEvent>
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
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var enumerator = DoRead().GetAsyncEnumerator(cancellationToken);
    var hasValue = false;
    Exception? lastException = null;
    try
    {
      hasValue = await enumerator.MoveNextAsync();
    }
    catch (Exception exception)
    {
      lastException = exception;
    }

    if (lastException is not null)
    {
      yield return new ReadAllMessage.Terminated(lastException);
    }

    do
    {
      if (hasValue)
      {
        yield return enumerator.Current;
      }

      try
      {
        hasValue = await enumerator.MoveNextAsync();
      }
      catch (Exception exception)
      {
        lastException = exception;
      }

      if (lastException is not null)
      {
        yield return new ReadAllMessage.Terminated(lastException);
      }
    } while (hasValue);

    async IAsyncEnumerable<ReadAllMessage> DoRead()
    {
      var direction = request.Direction == ReadDirection.Forwards ? Direction.Forwards : Direction.Backwards;
      var position = request.Relative switch
      {
        RelativePosition.Start => Position.Start,
        RelativePosition.End => Position.End,
        _ => request.Position switch
        {
          { } pos => new Position(pos, 0),
          _ => direction == Direction.Forwards ? Position.Start : Position.End
        }
      };

      await foreach (var msg in client
                       .ReadAllAsync(
                         direction,
                         position,
                         EventTypeFilter.ExcludeSystemEvents(),
                         cancellationToken: cancellationToken)
                       .Messages
                       .WithCancellation(cancellationToken))
      {
        switch (msg)
        {
          case StreamMessage.Ok:
            yield return new ReadAllMessage.ReadingStarted();
            break;
          case StreamMessage.Event(var re):
            yield return parser(re)
              .Match(
                ReadAllMessage (e) => new ReadAllMessage.AllEvent(
                  e.SwimLane,
                  e.GetEntityId(),
                  StoredEventMetadata.FromStorage(
                    EventMetadata.TryParse(
                      re.Event.Metadata.ToArray(),
                      re.Event.Created,
                      re.Event.Position.CommitPosition,
                      re.Event.EventNumber.ToUInt64()),
                    re.Event.EventId.ToGuid(),
                    re.Event.Position.CommitPosition,
                    re.Event.EventNumber.ToUInt64())),
                () => new ReadAllMessage.ToxicAllEvent(
                  re.Event.EventStreamId,
                  re.Event.EventStreamId,
                  re.Event.Metadata.ToArray(),
                  re.Event.Position.CommitPosition,
                  re.Event.EventNumber.ToUInt64()));
            break;
          case StreamMessage.AllStreamCheckpointReached(var pos):
            yield return new ReadAllMessage.Checkpoint(pos.CommitPosition);
            break;
        }
      }
    }
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
