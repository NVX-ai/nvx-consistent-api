using System.Runtime.CompilerServices;
using EventStore.Client;
using Grpc.Core;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI.Store.EventStoreDB;

// This is tightly bound to EventModelEvent, it can be made to work on any event interface.
public class EventStoreDbStore(string connectionString) : EventStore<EventModelEvent>
{
  private readonly EventStoreClient client = new(EventStoreClientSettings.Create(connectionString));
  private readonly Func<ResolvedEvent, Option<EventModelEvent>> parser = Parser();

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
    await foreach (var message in WithRetryOnConsumerTooSlow(DoRead, cancellationToken))
    {
      yield return message;
    }

    yield break;

    async IAsyncEnumerable<ReadAllMessage> DoRead()
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
          case StreamMessage.Ok:
            yield return new ReadAllMessage.ReadingStarted();
            break;
          case StreamMessage.Event(var re):
            yield return parser(re)
              .Match(
                ReadAllMessage (e) => new ReadAllMessage.AllEvent(
                  e.GetSwimLane(),
                  e.GetEntityId(),
                  StoredEventMetadata.FromStorage(
                    EventMetadata.TryParse(
                      re.Event.Metadata.ToArray(),
                      re.Event.Created,
                      re.Event.Position.CommitPosition,
                      re.Event.EventNumber.ToInt64()),
                    re.Event.EventId.ToGuid(),
                    re.Event.Position.CommitPosition,
                    re.Event.EventNumber.ToInt64())),
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

  public async IAsyncEnumerable<ReadStreamMessage<EventModelEvent>> Read(
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
        { } pos => StreamPosition.FromInt64((long)pos),
        _ => direction == Direction.Forwards ? StreamPosition.Start : StreamPosition.End
      }
    };

    await foreach (var message in WithRetryOnConsumerTooSlow(DoRead, cancellationToken))
    {
      yield return message;
    }

    async IAsyncEnumerable<ReadStreamMessage<EventModelEvent>> DoRead()
    {
      var streamName = $"{request.Swimlane}{request.Id.StreamId()}";

      yield return new ReadStreamMessage<EventModelEvent>.ReadingStarted();

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
          case StreamMessage.Event(var re):
            yield return parser(re)
              .Match(
                ReadStreamMessage<EventModelEvent> (e) => new ReadStreamMessage<EventModelEvent>.SolvedEvent(
                  e.GetSwimLane(),
                  e.GetEntityId(),
                  e,
                  CreateStoredEventMetadata(re)),
                () => new ReadStreamMessage<EventModelEvent>.ToxicEvent(
                  re.Event.EventStreamId,
                  null,
                  re.Event.Data.ToArray(),
                  re.Event.Metadata.ToArray(),
                  re.Event.Position.CommitPosition,
                  re.Event.EventNumber.ToUInt64()));
            position = re.Event.EventNumber;
            break;
          case StreamMessage.AllStreamCheckpointReached(var pos):
            yield return new ReadStreamMessage<EventModelEvent>.Checkpoint(pos.CommitPosition);
            break;
        }
      }
    }
  }

  public async IAsyncEnumerable<ReadAllMessage> Subscribe(
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

    async IAsyncEnumerable<ReadAllMessage> DoRead()
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
            yield return new ReadAllMessage.ReadingStarted();
            break;
          case StreamMessage.Event(var re):
            yield return parser(re)
              .Match(
                ReadAllMessage (e) => new ReadAllMessage.AllEvent(
                  e.GetSwimLane(),
                  e.GetEntityId(),
                  CreateStoredEventMetadata(re)),
                () => new ReadAllMessage.ToxicAllEvent(
                  re.Event.EventStreamId,
                  re.Event.EventStreamId,
                  re.Event.Metadata.ToArray(),
                  re.Event.Position.CommitPosition,
                  re.Event.EventNumber.ToUInt64()));
            position = FromAll.After(re.Event.Position);
            break;
          case StreamMessage.AllStreamCheckpointReached(var pos):
            yield return new ReadAllMessage.Checkpoint(pos.CommitPosition);
            break;
        }
      }
    }
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventModelEvent>> Subscribe(
    SubscribeStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var position = request.IsFromStart ? FromStream.Start : FromStream.End;

    await foreach (var message in WithRetryOnConsumerTooSlow(DoRead, cancellationToken))
    {
      yield return message;
    }

    async IAsyncEnumerable<ReadStreamMessage<EventModelEvent>> DoRead()
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
            yield return new ReadStreamMessage<EventModelEvent>.ReadingStarted();
            break;
          case StreamMessage.Event(var re):
            yield return parser(re)
              .Match(
                ReadStreamMessage<EventModelEvent> (e) => new ReadStreamMessage<EventModelEvent>.SolvedEvent(
                  e.GetSwimLane(),
                  e.GetEntityId(),
                  e,
                  CreateStoredEventMetadata(re)),
                () => new ReadStreamMessage<EventModelEvent>.ToxicEvent(
                  re.Event.EventStreamId,
                  null,
                  re.Event.Data.ToArray(),
                  re.Event.Metadata.ToArray(),
                  re.Event.Position.CommitPosition,
                  re.Event.EventNumber.ToUInt64()));
            position = FromStream.After(re.Event.EventNumber);
            break;
          case StreamMessage.AllStreamCheckpointReached(var pos):
            yield return new ReadStreamMessage<EventModelEvent>.Checkpoint(pos.CommitPosition);
            break;
          case StreamMessage.LastStreamPosition(var pos):
            position = FromStream.After(pos);
            break;
        }
      }
    }
  }

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
      var result = await client.AppendToStreamAsync(streamName, StreamRevision.FromInt64((long)position), eventData);
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

  private static Func<ResolvedEvent, Option<EventModelEvent>> Compose(
    params (string eventTypeName, Func<ResolvedEvent, Option<EventModelEvent>> parser)[] parsers
  )
  {
    var parsersDictionary = parsers.ToDictionary(tpl => tpl.eventTypeName, tpl => tpl.parser);
    return re => parsersDictionary.TryGetValue(re.Event.EventType, out var parser) ? parser(re) : None;
  }

  private static Func<ResolvedEvent, Option<EventModelEvent>> Parser()
  {
    return AllEventModelEventShapes().Select(ParserBuilder.Build).ToArray().Apply(Compose);

    static IEnumerable<Type> AllEventModelEventShapes()
    {
      var assemblies = AppDomain.CurrentDomain.GetAssemblies();
      var result = new HashSet<Type>();

      foreach (var assembly in assemblies)
      {
        // There is a bug with the test runner that prevents loading some types
        // from system data while running tests.
        if (assembly.FullName?.StartsWith("System.Data.") ?? false)
        {
          continue;
        }

        var types = assembly.GetTypes().Where(t => t.GetInterfaces().Contains(typeof(EventModelEvent)) && t.IsClass);

        foreach (var type in types)
        {
          result.Add(type);
        }
      }

      return result;
    }
  }

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

      if (lastException is not null)
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

  private static T CreateTerminatedMessage<T>(Exception exception) where T : class =>
    typeof(T) == typeof(ReadAllMessage)
      ? (T)(object)new ReadAllMessage.Terminated(exception)
      : (T)(object)new ReadStreamMessage<EventModelEvent>.Terminated(exception);

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
