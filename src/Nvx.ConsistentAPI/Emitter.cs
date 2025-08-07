using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI;

public class Emitter(EventStore<EventModelEvent> store, ILogger logger)
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

  private AsyncResult<string, ApiError> AnyState(EventModelEvent[] events, EventContext? context) =>
    GetId(events)
      .Bind(id =>
      {
        return Go();

        async Task<Result<string, ApiError>> Go()
        {
          var swimlane = events.First().SwimLane;
          var streamId = events.First().GetEntityId();
          return await store
            .Insert(
              new InsertionPayload<EventModelEvent>(
                swimlane,
                streamId,
                new AnyStreamState(),
                context?.RelatedUserSub,
                context?.CorrelationId,
                context?.CausationId,
                events))
            .Match<Result<string, ApiError>>(
              async r =>
              {
                if (!events.Any(e => e.GetType().GetInterfaces().Any(i => i == typeof(EventModelSnapshotEvent))))
                {
                  return id;
                }

                await store.TruncateStream(swimlane, streamId, r.StreamPosition);
                return id;
              },
              f => Task.FromResult<Result<string, ApiError>>(
                new DisasterError($"Failed event insertion on {swimlane} {streamId} ({f.GetType().Name})")));
        }
      });

  private AsyncResult<string, ApiError> Create(EventModelEvent[] events, EventContext? context) =>
    GetId(events)
      .Bind(id =>
      {
        return Go();

        async Task<Result<string, ApiError>> Go()
        {
          var swimlane = events.First().SwimLane;
          var streamId = events.First().GetEntityId();
          return await store
            .Insert(
              new InsertionPayload<EventModelEvent>(
                swimlane,
                streamId,
                new StreamCreation(),
                context?.RelatedUserSub,
                context?.CorrelationId,
                context?.CausationId,
                events))
            .Match<Result<string, ApiError>>(
              _ => id,
              f => f.Match<Result<string, ApiError>>(
                () => new ConflictError($"Tried to create swimlane {swimlane} {streamId} that already exists"),
                () => new DisasterError($"Failed event insertion on {swimlane} {streamId} ({f.GetType().Name})"),
                () => new DisasterError($"Failed event insertion on {swimlane} {streamId} ({f.GetType().Name})")));
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
          var swimlane = events.First().SwimLane;
          var streamId = events.First().GetEntityId();
          return await store
            .Insert(
              new InsertionPayload<EventModelEvent>(
                swimlane,
                streamId,
                new InsertAfter((ulong)expectedRevision),
                context?.RelatedUserSub,
                context?.CorrelationId,
                context?.CausationId,
                events))
            .Match<Result<string, ApiError>>(
              async r =>
              {
                if (!events.Any(e => e.GetType().GetInterfaces().Any(i => i == typeof(EventModelSnapshotEvent))))
                {
                  return id;
                }

                await store.TruncateStream(swimlane, streamId, r.StreamPosition);
                return id;
              },
              f => f
                .Match<Result<string, ApiError>>(
                  () => new ConflictError($"Swimlane {swimlane} {streamId} was in the wrong revision"),
                  () => new DisasterError($"Failed event insertion on {swimlane} {streamId} ({f.GetType().Name})"),
                  () => new DisasterError($"Failed event insertion on {swimlane} {streamId} ({f.GetType().Name})"))
                .ToTask());
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
    while (i < 1000)
    {
      try
      {
        // async await is needed for the try catch to work
        var result = await encapsulatedDecider().Bind(async ei => await HandleInsert(ei));
        if (!result.Match(
              _ => false,
              e => e.Match(
                ae => ae is ConflictError ce
                      && ce.Message.StartsWith("Swimlane")
                      && ce.Message.EndsWith(" was in the wrong revision"),
                _ => false)))
        {
          return result;
        }

        i++;
        if (shouldSkipRetry)
        {
          return new DisasterError("Wrong revision when trying to emit").Apply(First<ApiError, TError>);
        }

        await Task.Delay(Random.Shared.Next(25, 250));
        continue;

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
