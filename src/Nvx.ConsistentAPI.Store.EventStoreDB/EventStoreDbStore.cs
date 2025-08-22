using System.Runtime.CompilerServices;
using EventStore.Client;
using Grpc.Core;
using Nvx.ConsistentAPI.Store.Events;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI.Store.EventStoreDB;

// This is tightly bound to EventModelEvent, it can be made to work on any event interface.
public class EventStoreDbStore<EventInterface>(
  string connectionString,
  Func<string, byte[], Option<(EventInterface evt, StrongId streamId)>> deserializer,
  Func<EventInterface, (string typeName, byte[] data)> serializer)
  : EventStore<EventInterface> where EventInterface : HasSwimlane, HasEntityId
{
  private readonly EventStoreClient client = new(EventStoreClientSettings.Create(connectionString));

  public Task Initialize(CancellationToken cancellationToken = default) => Task.CompletedTask;

  public async Task TruncateStream(string swimlane, StrongId id, long truncateBefore)
  {
    var streamName = $"{swimlane}{id.StreamId()}";

    var read = client.ReadStreamAsync(Direction.Forwards, streamName, StreamPosition.Start, 1);

    if (await read.ReadState == ReadState.StreamNotFound)
    {
      return;
    }

    var currentStreamMetadata = await client.GetStreamMetadataAsync(streamName).Map(mdr => mdr.Metadata);
    await client.SetStreamMetadataAsync(
      streamName,
      StreamState.Any,
      new StreamMetadata(
        currentStreamMetadata.MaxCount,
        currentStreamMetadata.MaxAge,
        StreamPosition.FromInt64(truncateBefore),
        currentStreamMetadata.CacheControl,
        currentStreamMetadata.Acl,
        currentStreamMetadata.CustomMetadata));
  }

  public AsyncResult<InsertionSuccess, InsertionFailure> Insert(InsertionPayload<EventInterface> payload)
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

  public async IAsyncEnumerable<ReadAllMessage<EventInterface>> Read(
    ReadAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await foreach (var message in WithRetryOnConsumerTooSlow(DoRead, cancellationToken))
    {
      yield return message;
    }

    yield break;

    async IAsyncEnumerable<ReadAllMessage<EventInterface>> DoRead()
    {
      var direction = request.Direction == ReadDirection.Forwards ? Direction.Forwards : Direction.Backwards;
      var position = request.Relative switch
      {
        RelativePosition.Start => Position.Start,
        RelativePosition.End => Position.End,
        _ => request.Position switch
        {
          { } pos => new Position(pos, pos),
          _ => direction == Direction.Forwards ? Position.Start : Position.End
        }
      };

      var swimlanes = request.Swimlanes ?? [];
      var filter = swimlanes.Length > 0
        ? StreamFilter.Prefix(swimlanes)
        : EventTypeFilter.ExcludeSystemEvents();

      await foreach (var msg in client
                       .ReadAllAsync(
                         direction,
                         position,
                         filter,
                         cancellationToken: cancellationToken)
                       .Messages
                       .WithCancellation(cancellationToken))
      {
        switch (msg)
        {
          case StreamMessage.SubscriptionConfirmation:
          case StreamMessage.Ok:
            yield return new ReadAllMessage<EventInterface>.ReadingStarted();
            break;
          case StreamMessage.Event(var re):
            yield return Parse(re)
              .Match(
                ReadAllMessage<EventInterface> (e) => new ReadAllMessage<EventInterface>.AllEvent(
                  e.GetSwimlane(),
                  e.GetEntityId(),
                  e,
                  StoredEventMetadata.FromStorage(
                    EventMetadata.TryParse(
                      re.Event.Metadata.ToArray(),
                      re.Event.Created,
                      re.Event.Position.CommitPosition,
                      re.Event.EventNumber.ToInt64()),
                    re.Event.EventId.ToGuid(),
                    re.Event.Position.CommitPosition,
                    re.Event.EventNumber.ToInt64())),
                () => new ReadAllMessage<EventInterface>.ToxicAllEvent(
                  re.Event.EventStreamId,
                  re.Event.Metadata.ToArray(),
                  re.Event.Position.CommitPosition,
                  re.Event.EventNumber.ToInt64()));
            break;
          case StreamMessage.AllStreamCheckpointReached(var pos):
            yield return new ReadAllMessage<EventInterface>.Checkpoint(pos.CommitPosition);
            break;
          case StreamMessage.CaughtUp:
            yield return new ReadAllMessage<EventInterface>.CaughtUp();
            break;
          case StreamMessage.FellBehind:
            yield return new ReadAllMessage<EventInterface>.FellBehind();
            break;
        }
      }
    }
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Read(
    ReadStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var direction = request.Direction == ReadDirection.Forwards ? Direction.Forwards : Direction.Backwards;
    var position = request.Position switch
    {
      RelativePosition.Start => StreamPosition.Start,
      RelativePosition.End => StreamPosition.End,
      _ => request.StreamPosition switch
      {
        { } pos => StreamPosition.FromInt64(pos),
        _ => direction == Direction.Forwards ? StreamPosition.Start : StreamPosition.End
      }
    };

    await foreach (var message in WithRetryOnConsumerTooSlow(DoRead, cancellationToken))
    {
      yield return message;
    }

    yield break;

    async IAsyncEnumerable<ReadStreamMessage<EventInterface>> DoRead()
    {
      var streamName = $"{request.Swimlane}{request.Id.StreamId()}";

      yield return new ReadStreamMessage<EventInterface>.ReadingStarted();

      await foreach (var msg in client
                       .ReadStreamAsync(
                         direction,
                         streamName,
                         position,
                         cancellationToken: cancellationToken)
                       .Messages
                       .WithCancellation(cancellationToken))
      {
        switch (msg)
        {
          case StreamMessage.SubscriptionConfirmation:
          case StreamMessage.Ok:
            yield return new ReadStreamMessage<EventInterface>.ReadingStarted();
            break;
          case StreamMessage.Event(var re):
            yield return Parse(re)
              .Match(
                ReadStreamMessage<EventInterface> (e) => new ReadStreamMessage<EventInterface>.SolvedEvent(
                  e.GetSwimlane(),
                  e.GetEntityId(),
                  e,
                  CreateStoredEventMetadata(re)),
                () => new ReadStreamMessage<EventInterface>.ToxicEvent(
                  re.Event.EventStreamId,
                  re.Event.Data.ToArray(),
                  re.Event.Metadata.ToArray(),
                  re.Event.Position.CommitPosition,
                  re.Event.EventNumber.ToInt64()));
            position = re.Event.EventNumber;
            break;
          case StreamMessage.AllStreamCheckpointReached(var pos):
            yield return new ReadStreamMessage<EventInterface>.Checkpoint(pos.CommitPosition);
            break;
          case StreamMessage.FellBehind:
            yield return new ReadStreamMessage<EventInterface>.FellBehind();
            break;
          case StreamMessage.CaughtUp:
            yield return new ReadStreamMessage<EventInterface>.CaughtUp();
            break;
        }
      }
    }
  }

  public async IAsyncEnumerable<ReadAllMessage<EventInterface>> Subscribe(
    SubscribeAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var filterOptions = new SubscriptionFilterOptions(
      request.Swimlanes?.Length > 0
        ? StreamFilter.Prefix(request.Swimlanes)
        : EventTypeFilter.ExcludeSystemEvents());

    var position = request.Position switch
    {
      0 => FromAll.Start,
      null => FromAll.End,
      { } value => FromAll.After(new Position(value, value))
    };

    await foreach (var message in WithRetryOnConsumerTooSlow(DoRead, cancellationToken))
    {
      yield return message;
    }

    yield break;

    async IAsyncEnumerable<ReadAllMessage<EventInterface>> DoRead()
    {
      await foreach (var msg in client
                       .SubscribeToAll(
                         position,
                         filterOptions: filterOptions,
                         cancellationToken: cancellationToken)
                       .Messages.WithCancellation(cancellationToken))
      {
        switch (msg)
        {
          case StreamMessage.Ok:
          case StreamMessage.SubscriptionConfirmation:
            yield return new ReadAllMessage<EventInterface>.ReadingStarted();
            break;
          case StreamMessage.Event(var re):
            yield return Parse(re)
              .Match(
                ReadAllMessage<EventInterface> (e) => new ReadAllMessage<EventInterface>.AllEvent(
                  e.GetSwimlane(),
                  e.GetEntityId(),
                  e,
                  CreateStoredEventMetadata(re)),
                () => new ReadAllMessage<EventInterface>.ToxicAllEvent(
                  re.Event.EventStreamId,
                  re.Event.Metadata.ToArray(),
                  re.Event.Position.CommitPosition,
                  re.Event.EventNumber.ToInt64()));
            position = FromAll.After(re.Event.Position);
            break;
          case StreamMessage.AllStreamCheckpointReached(var pos):
            yield return new ReadAllMessage<EventInterface>.Checkpoint(pos.CommitPosition);
            break;
          case StreamMessage.CaughtUp:
            yield return new ReadAllMessage<EventInterface>.CaughtUp();
            break;
          case StreamMessage.FellBehind:
            yield return new ReadAllMessage<EventInterface>.FellBehind();
            break;
        }
      }
    }
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Subscribe(
    SubscribeStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var position = request.IsFromStart ? FromStream.Start : FromStream.End;

    await foreach (var message in WithRetryOnConsumerTooSlow(DoRead, cancellationToken))
    {
      yield return message;
    }

    yield break;

    async IAsyncEnumerable<ReadStreamMessage<EventInterface>> DoRead()
    {
      var streamName = $"{request.Swimlane}{request.Id.StreamId()}";

      await foreach (var msg in client
                       .SubscribeToStream(
                         streamName,
                         position,
                         cancellationToken: cancellationToken)
                       .Messages
                       .WithCancellation(cancellationToken))
      {
        switch (msg)
        {
          case StreamMessage.SubscriptionConfirmation:
          case StreamMessage.Ok:
            yield return new ReadStreamMessage<EventInterface>.ReadingStarted();
            break;
          case StreamMessage.Event(var re):
            yield return Parse(re)
              .Match(
                ReadStreamMessage<EventInterface> (e) => new ReadStreamMessage<EventInterface>.SolvedEvent(
                  e.GetSwimlane(),
                  e.GetEntityId(),
                  e,
                  CreateStoredEventMetadata(re)),
                () => new ReadStreamMessage<EventInterface>.ToxicEvent(
                  re.Event.EventStreamId,
                  re.Event.Data.ToArray(),
                  re.Event.Metadata.ToArray(),
                  re.Event.Position.CommitPosition,
                  re.Event.EventNumber.ToInt64()));
            position = FromStream.After(re.Event.EventNumber);
            break;
          case StreamMessage.AllStreamCheckpointReached(var pos):
            yield return new ReadStreamMessage<EventInterface>.Checkpoint(pos.CommitPosition);
            break;
          case StreamMessage.LastStreamPosition(var pos):
            position = FromStream.After(pos);
            break;
        }
      }
    }
  }

  private Option<EventInterface> Parse(ResolvedEvent re) =>
    deserializer(re.Event.EventType, re.Event.Data.ToArray()).Select(t => t.evt);

  private async Task<Result<InsertionSuccess, InsertionFailure>> AnyState(
    IEnumerable<EventData> eventData,
    string streamName)
  {
    try
    {
      var result = await client.AppendToStreamAsync(streamName, StreamState.Any, eventData);
      // NextExpectedStreamRevision is the position of the last inserted event.
      return new InsertionSuccess(result.LogPosition.CommitPosition, result.NextExpectedStreamRevision.ToInt64());
    }
    catch
    {
      return new InsertionFailure.InsertionFailed();
    }
  }

  private async Task<Result<InsertionSuccess, InsertionFailure>> InsertAfter(
    IEnumerable<EventData> eventData,
    string streamName,
    long position)
  {
    try
    {
      var result = await client.AppendToStreamAsync(streamName, StreamRevision.FromInt64(position), eventData);
      // NextExpectedStreamRevision is the position of the last inserted event.
      return new InsertionSuccess(result.LogPosition.CommitPosition, result.NextExpectedStreamRevision.ToInt64());
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
      return new InsertionSuccess(result.LogPosition.CommitPosition, result.NextExpectedStreamRevision.ToInt64());
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
      return new InsertionSuccess(result.LogPosition.CommitPosition, result.NextExpectedStreamRevision.ToInt64());
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

  private IEnumerable<EventData> ToEventData(
    (EventInterface Event, EventInsertionMetadataPayload Metadata)[] insertions) =>
    insertions.Select(i =>
      serializer(i.Event)
        .Apply(eventTuple =>
          new EventData(
            Uuid.FromGuid(i.Metadata.EventId),
            eventTuple.typeName,
            eventTuple.data,
            new EventMetadata(
                i.Metadata.CreatedAt,
                i.Metadata.CorrelationId,
                i.Metadata.CausationId,
                i.Metadata.RelatedUserSub,
                null,
                null)
              .ToBytes()
          )));

  private static async IAsyncEnumerable<T> WithRetryOnConsumerTooSlow<T>(
    Func<IAsyncEnumerable<T>> enumerableFactory,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    where T : class
  {
    bool hadConsumerTooSlowException;
    do
    {
      var enumerator = enumerableFactory().GetAsyncEnumerator(cancellationToken);
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

      hadConsumerTooSlowException = IsConsumerTooSlowException(lastException);

      if (lastException is not null && !hadConsumerTooSlowException)
      {
        yield return CreateTerminatedMessage<T>(lastException);
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

        hadConsumerTooSlowException = IsConsumerTooSlowException(lastException);

        if (lastException is not null && !hadConsumerTooSlowException)
        {
          yield return CreateTerminatedMessage<T>(lastException);
        }
      } while (hasValue);
    } while (hadConsumerTooSlowException);
  }

  private static bool IsConsumerTooSlowException(Exception? exception) =>
    exception is RpcException { Status.StatusCode: StatusCode.Aborted } rpcEx
    && rpcEx.Status.Detail.Contains("too slow");

  internal static T CreateTerminatedMessage<T>(Exception exception) where T : class =>
    typeof(T) == typeof(ReadAllMessage<EventInterface>)
      ? (T)(object)new ReadAllMessage<EventInterface>.Terminated(exception)
      : (T)(object)new ReadStreamMessage<EventInterface>.Terminated(exception);

  private static StoredEventMetadata CreateStoredEventMetadata(ResolvedEvent re) =>
    StoredEventMetadata.FromStorage(
      EventMetadata.TryParse(
        re.Event.Metadata.ToArray(),
        re.Event.Created,
        re.Event.Position.CommitPosition,
        re.Event.EventNumber.ToInt64()),
      re.Event.EventId.ToGuid(),
      re.Event.Position.CommitPosition,
      re.Event.EventNumber.ToInt64());
}
