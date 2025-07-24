using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI;

public class Emitter(EventStoreClient client, ILogger logger)
{
  private static AsyncResult<string, ApiError> GetId(EventModelEvent[] events)
  {
    if (events.Length == 0)
    {
      return new DisasterError("No events were emitted");
    }

    var streamName = events.First().GetStreamName();
    return events.All(e => e.GetStreamName() == streamName)
      ? events.First().GetEntityId().StreamId()
      : new DisasterError("All events must belong to the same stream");
  }

  internal static IEnumerable<EventData> ToEventData(IEnumerable<EventModelEvent> events, EventContext? context) =>
    events.Select(e =>
      new EventData(
        Uuid.NewUuid(),
        e.EventType,
        e.ToBytes(),
        new EventMetadata(
            DateTime.UtcNow,
            context?.CorrelationId,
            context?.CausationId,
            context?.RelatedUserSub,
            null,
            null)
          .ToBytes()
      ));

  private AsyncResult<string, ApiError> AnyState(EventModelEvent[] events, EventContext? context) =>
    GetId(events)
      .Bind(id =>
      {
        return Go();

        async Task<Result<string, ApiError>> Go()
        {
          var streamName = events.First().GetStreamName();
          var eventData = ToEventData(events, context);
          var result = await client.AppendToStreamAsync(streamName, StreamState.Any, eventData);

          if (!events.Any(e => e.GetType().GetInterfaces().Any(i => i == typeof(EventModelSnapshotEvent))))
          {
            return id;
          }

          var currentStreamMetadata = await client.GetStreamMetadataAsync(streamName).Map(mdr => mdr.Metadata);
          var newMetadata = new StreamMetadata(
            currentStreamMetadata.MaxCount,
            currentStreamMetadata.MaxAge,
            result.NextExpectedStreamRevision.ToUInt64(),
            currentStreamMetadata.CacheControl,
            currentStreamMetadata.Acl,
            currentStreamMetadata.CustomMetadata);
          await client.SetStreamMetadataAsync(
            streamName,
            StreamState.Any,
            newMetadata
          );

          return id;
        }
      });

  private AsyncResult<string, ApiError> Create(EventModelEvent[] events, EventContext? context) =>
    GetId(events)
      .Bind(id =>
      {
        return Go();

        async Task<Result<string, ApiError>> Go()
        {
          var streamName = events.First().GetStreamName();
          var eventData = ToEventData(events, context);
          await client.AppendToStreamAsync(streamName, StreamState.NoStream, eventData);
          return id;
        }
      });

  private AsyncResult<string, ApiError> Existing(
    EventModelEvent[] events,
    long expectedRevision,
    EventContext? context) =>
    GetId(events)
      .Bind(id =>
      {
        return Go();

        async Task<Result<string, ApiError>> Go()
        {
          var streamName = events.First().GetStreamName();
          var eventData = ToEventData(events, context);
          var result = await client.AppendToStreamAsync(
            streamName,
            StreamRevision.FromInt64(expectedRevision),
            eventData);

          if (!events.Any(e => e.GetType().GetInterfaces().Any(i => i == typeof(EventModelSnapshotEvent))))
          {
            return id;
          }

          var currentStreamMetadata = await client.GetStreamMetadataAsync(streamName).Map(mdr => mdr.Metadata);
          await client.SetStreamMetadataAsync(
            streamName,
            StreamState.Any,
            new StreamMetadata(
              currentStreamMetadata.MaxCount,
              currentStreamMetadata.MaxAge,
              result.NextExpectedStreamRevision.ToUInt64(),
              currentStreamMetadata.CacheControl,
              currentStreamMetadata.Acl,
              currentStreamMetadata.CustomMetadata)
          );

          return id;
        }
      });

  private AsyncResult<string, ApiError> MultiStream(EventModelEvent[] events, EventContext? context)
  {
    if (events.Length == 0)
    {
      return new DisasterError("No events were emitted");
    }

    return events
      .Select(Insert)
      .Parallel()
      .Map(results => results.Aggregate(
        Ok<string, ApiError>(""),
        (acc, r) => acc.Bind(value => string.IsNullOrEmpty(value) ? r : value)))
      .Async();

    Func<Task<Result<string, ApiError>>> Insert(EventModelEvent @event) =>
      async () => await AnyState([@event], context);
  }


  public AsyncResult<Unit, Unit> TryInsert(EventModelEvent @event, long expectedRevision, EventContext? context)
  {
    return Go();

    async Task<Result<Unit, Unit>> Go()
    {
      try
      {
        return await Existing([@event], expectedRevision, context).Map(_ => unit).MapError(_ => unit);
      }
      catch
      {
        return Error<Unit, Unit>(unit);
      }
    }
  }

  public async Task<Result<string, Du<ApiError, TError>>> Emit<TError>(
    Func<AsyncResult<EventInsertion, Du<ApiError, TError>>> encapsulatedDecider,
    EventContext? context = null,
    bool shouldSkipRetry = false
  )
  {
    var i = 0;
    var random = new Random();
    while (i < 1000)
    {
      try
      {
        // async await is needed for the try catch to work
        return await encapsulatedDecider().Bind(async ei => await HandleInsert(ei));

        AsyncResult<string, Du<ApiError, TError>> HandleInsert(EventInsertion insertion) =>
          insertion switch
          {
            AnyState a => AnyState(a.Events, context).MapError(First<ApiError, TError>),
            CreateStream c => Create(c.Events, context).MapError(First<ApiError, TError>),
            ExistingStream e => Existing(e.Events, e.ExpectedRevision, context).MapError(First<ApiError, TError>),
            MultiStream m => MultiStream(m.Events, context).MapError(First<ApiError, TError>),
            _ => new DisasterError("Unknown Event Insertion Type").Apply(First<ApiError, TError>).ToTask()
          };
      }
      catch (WrongExpectedVersionException)
      {
        i++;
        if (shouldSkipRetry)
        {
          return new DisasterError("Wrong revision when trying to emit").Apply(First<ApiError, TError>);
        }

        await Task.Delay(random.Next(25, 250));
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Unexpected error when trying to emit an event");
        return new DisasterError(ex.Message).Apply(First<ApiError, TError>);
      }
    }

    return new DisasterError("Could not process the command").Apply(First<ApiError, TError>);
  }

  public Task<Result<string, ApiError>> Emit(
    Func<AsyncResult<EventInsertion, ApiError>> encapsulatedDecider,
    EventContext? context = null,
    bool shouldSkipRetry = false
  ) =>
    Emit(() => encapsulatedDecider().MapError(First<ApiError, ApiError>), context, shouldSkipRetry)
      .Async()
      .MapError(du => du.Match(Id, Id))
      .ToTask();
}

public record EventContext(string? CorrelationId, string? CausationId, string? RelatedUserSub);
