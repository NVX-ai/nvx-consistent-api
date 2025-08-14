using System.Runtime.CompilerServices;
using DeFuncto;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI.Store.InMemory;

public class InMemoryEventStore<EventInterface> : EventStore<EventInterface>
{
  // It is static so if it's instanced anywhere else in the system, the state is shared, this is meant to simulate
  // storage.
  private static readonly List<StoredEvent> Events = [];
  // ReSharper disable once StaticMemberInGenericType
  private static readonly SemaphoreSlim Semaphore = new(1, 1);

  public Task Initialize(CancellationToken cancellationToken = default) => Task.CompletedTask;

  public AsyncResult<InsertionSuccess, InsertionFailure> Insert(InsertionPayload<EventInterface> payload)
  {
    Semaphore.Wait();
    var latestGlobalPosition = Events.Select(se => se.Metadata.GlobalPosition).LastOrDefault();
    var latestStreamPosition = Events
      .LastOrDefault(se => se.StreamId == payload.StreamId && se.Swimlane == payload.Swimlane)
      ?.Metadata.StreamPosition;

    if ((payload.InsertionType is StreamCreation && latestStreamPosition != 0)
        || (payload.InsertionType is ExistingStream && latestStreamPosition == 0)
        || (payload.InsertionType is InsertAfter(var after) && after != latestStreamPosition))
    {
      Semaphore.Release();
      return new InsertionFailure.ConsistencyCheckFailed();
    }

    var nonExistingEvents =
      from insertion in payload.Insertions
      where Events.All(e => e.Metadata.EventId != insertion.Metadata.EventId)
      select insertion;

    Events.AddRange(
      from tuple in nonExistingEvents.Select((t, i) => (t, i: (long)i))
      let evt = tuple.t.Event
      let md = tuple.t.Metadata
      let index = tuple.i
      select new StoredEvent(
        payload.Swimlane,
        payload.StreamId,
        evt,
        new StoredEventMetadata(
          md.EventId,
          md.RelatedUserSub,
          md.CorrelationId,
          md.CausationId,
          md.CreatedAt,
          latestGlobalPosition + (ulong)index + 1UL,
          (latestStreamPosition ?? 0) + index + (latestStreamPosition.HasValue ? 1L : 0))));

    var success = new InsertionSuccess(
      Events.Select(e => e.Metadata.GlobalPosition).LastOrDefault(),
      Events.Select(e => e.Metadata.StreamPosition).LastOrDefault());
    Semaphore.Release();
    return success;
  }

  public async IAsyncEnumerable<ReadAllMessage<EventInterface>> Read(
    ReadAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    try
    {
      await Semaphore.WaitAsync(cancellationToken);
      yield return new ReadAllMessage<EventInterface>.ReadingStarted();
      ulong? index = request.Relative switch
      {
        RelativePosition.Start => 0,
        RelativePosition.End => (ulong)Events.Count,
        _ => request.Direction switch
        {
          ReadDirection.Backwards => request.Position - 1,
          _ => request.Position
        }
      };

      ulong? edge = request.Direction switch
      {
        ReadDirection.Backwards => null,
        _ => (ulong)Events.Count + 1
      };

      var lanes = request.Swimlanes ?? [];
      var hasSwimlanes = lanes.Length > 0;

      while (index != edge)
      {
        var storedEvent = Events.FirstOrDefault(e => e.Metadata.GlobalPosition == index);
        index = request.Direction switch
        {
          ReadDirection.Backwards => index == 0 ? null : index - 1,
          _ => index + 1
        };

        if (storedEvent is null)
        {
          continue;
        }

        if (hasSwimlanes && !lanes.Contains(storedEvent.Swimlane))
        {
          continue;
        }

        yield return new ReadAllMessage<EventInterface>.AllEvent(
          storedEvent.Swimlane,
          storedEvent.StreamId,
          storedEvent.Event,
          storedEvent.Metadata);
      }
    }
    finally
    {
      Semaphore.Release();
    }
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Read(
    ReadStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await Semaphore.WaitAsync(cancellationToken);
    try
    {
      yield return new ReadStreamMessage<EventInterface>.ReadingStarted();
      var streamEvents = Events
        .Where(se => se.StreamId == request.Id)
        .Where(se => se.Swimlane == request.Swimlane);

      var orderedEvents = request.Direction switch
      {
        ReadDirection.Backwards => streamEvents.Reverse(),
        _ => streamEvents
      };

      foreach (var storedEvent in orderedEvents)
      {
        switch (request.Direction)
        {
          case ReadDirection.Backwards
            when request.StreamPosition.HasValue
                 && storedEvent.Metadata.StreamPosition > request.StreamPosition:
          case ReadDirection.Forwards
            when request.StreamPosition.HasValue
                 && storedEvent.Metadata.StreamPosition < request.StreamPosition:
            continue;
          default:
            yield return new ReadStreamMessage<EventInterface>.SolvedEvent(
              storedEvent.Swimlane,
              storedEvent.StreamId,
              storedEvent.Event,
              storedEvent.Metadata);
            break;
        }
      }
    }
    finally
    {
      Semaphore.Release();
    }
  }

  public async IAsyncEnumerable<ReadAllMessage<EventInterface>> Subscribe(
    SubscribeAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var currentGlobalPosition = request.Position ?? Events.Select(se => se.Metadata.GlobalPosition).LastOrDefault();
    var lanes = request.Swimlanes ?? [];
    var hasSwimlanes = lanes.Length > 0;
    var hasStarted = false;
    while (true)
    {
      await Semaphore.WaitAsync(cancellationToken);
      try
      {
        if (!hasStarted)
        {
          yield return new ReadAllMessage<EventInterface>.ReadingStarted();
          hasStarted = true;
        }

        var nextEvent = Events
          .Where(se => se.Metadata.GlobalPosition > currentGlobalPosition)
          .Select(se => new ReadAllMessage<EventInterface>.AllEvent(
            se.Swimlane,
            se.StreamId,
            se.Event,
            se.Metadata))
          .FirstOrDefault();

        if (nextEvent is null)
        {
          await Task.Delay(5, cancellationToken);
          continue;
        }

        currentGlobalPosition = nextEvent.Metadata.GlobalPosition;

        if (hasSwimlanes && !lanes.Contains(nextEvent.Swimlane))
        {
          continue;
        }

        yield return nextEvent;
      }
      finally
      {
        Semaphore.Release();
      }
    }
    // ReSharper disable once IteratorNeverReturns
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Subscribe(
    SubscribeStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    long? currentStreamPosition = request.IsFromStart
      ? null
      : Events
        .Where(se => se.StreamId == request.Id)
        .Where(se => se.Swimlane == request.Swimlane)
        .Select(se => se.Metadata.StreamPosition)
        .LastOrDefault();

    var hasStarted = false;

    while (true)
    {
      await Semaphore.WaitAsync(cancellationToken);
      try
      {
        if (!hasStarted)
        {
          yield return new ReadStreamMessage<EventInterface>.ReadingStarted();
          hasStarted = true;
        }

        var nextEvent = Events
          .Where(se => se.StreamId == request.Id)
          .Where(se => se.Swimlane == request.Swimlane)
          .Where(se => se.Metadata.StreamPosition > currentStreamPosition || currentStreamPosition == null)
          .Select(se => new ReadStreamMessage<EventInterface>.SolvedEvent(
            se.Swimlane,
            se.StreamId,
            se.Event,
            se.Metadata))
          .FirstOrDefault();

        if (nextEvent is null)
        {
          await Task.Delay(5, cancellationToken);
          continue;
        }

        currentStreamPosition = nextEvent.Metadata.StreamPosition;
        yield return nextEvent;
      }
      finally
      {
        Semaphore.Release();
      }
    }
    // ReSharper disable once IteratorNeverReturns
  }

  public async Task TruncateStream(string swimlane, StrongId id, long truncateBefore)
  {
    await Semaphore.WaitAsync();
    try
    {
      Events.RemoveAll(e => e.Swimlane == swimlane && e.StreamId == id && e.Metadata.StreamPosition < truncateBefore);
    }
    finally
    {
      Semaphore.Release();
    }
  }

  private record StoredEvent(string Swimlane, StrongId StreamId, EventInterface Event, StoredEventMetadata Metadata);
}
