using System.Runtime.CompilerServices;
using DeFuncto;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI.Store.InMemory;

public class InMemoryEventStore<EventInterface> : EventStore<EventInterface>
{
  private readonly List<StoredEvent> events = [];
  private readonly SemaphoreSlim semaphore = new(1, 1);

  public Task Initialize(CancellationToken cancellationToken = default) => Task.CompletedTask;

  public AsyncResult<InsertionSuccess, InsertionFailure> Insert(InsertionPayload<EventInterface> payload)
  {
    try
    {
      semaphore.Wait();
      var latestGlobalPosition = events.Select(se => se.Metadata.GlobalPosition).LastOrDefault();
      var latestStreamPosition = events
        .LastOrDefault(se => se.StreamId == payload.StreamId && se.Swimlane == payload.Swimlane)
        ?.Metadata.StreamPosition;

      if ((payload.InsertionType is StreamCreation && latestStreamPosition != null)
          || (payload.InsertionType is ExistingStream && latestStreamPosition == null)
          || (payload.InsertionType is InsertAfter(var after) && after != latestStreamPosition))
      {
        semaphore.Release();
        return new InsertionFailure.ConsistencyCheckFailed();
      }

      var nonExistingEvents =
        from insertion in payload.Insertions
        where events.All(e => e.Metadata.EventId != insertion.Metadata.EventId)
        select insertion;

      events.AddRange(
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

      semaphore.Release();
      return new InsertionSuccess(
        events.Select(e => e.Metadata.GlobalPosition).LastOrDefault(),
        events.Select(e => e.Metadata.StreamPosition).LastOrDefault());
    }
    catch
    {
      semaphore.Release();
      return new InsertionFailure.InsertionFailed();
    }
  }

  public async IAsyncEnumerable<ReadAllMessage<EventInterface>> Read(
    ReadAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    // await semaphore.WaitAsync(cancellationToken);
    yield return new ReadAllMessage<EventInterface>.ReadingStarted();
    ulong? index = request.Relative switch
    {
      RelativePosition.Start => 0,
      RelativePosition.End => (ulong)events.Count,
      _ => request.Direction switch
      {
        ReadDirection.Backwards => request.Position - 1,
        _ => request.Position
      }
    };

    ulong? edge = request.Direction switch
    {
      ReadDirection.Backwards => null,
      _ => (ulong)events.Count + 1
    };

    var lanes = request.Swimlanes ?? [];
    var hasSwimlanes = lanes.Length > 0;

    while (index != edge)
    {
      var storedEvent = events.FirstOrDefault(e => e.Metadata.GlobalPosition == index);
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

    // semaphore.Release();
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Read(
    ReadStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    // await semaphore.WaitAsync(cancellationToken);

    yield return new ReadStreamMessage<EventInterface>.ReadingStarted();
    var streamEvents = events
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

    // semaphore.Release();
  }

  public async IAsyncEnumerable<ReadAllMessage<EventInterface>> Subscribe(
    SubscribeAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var currentGlobalPosition = request.Position ?? events.Select(se => se.Metadata.GlobalPosition).LastOrDefault();
    var lanes = request.Swimlanes ?? [];
    var hasSwimlanes = lanes.Length > 0;
    var hasStarted = false;
    while (true)
    {
      // await semaphore.WaitAsync(cancellationToken);
      if (!hasStarted)
      {
        yield return new ReadAllMessage<EventInterface>.ReadingStarted();
        hasStarted = true;
      }

      var nextEvent = events
        .Where(se => se.Metadata.GlobalPosition > currentGlobalPosition)
        .Select(se => new ReadAllMessage<EventInterface>.AllEvent(
          se.Swimlane,
          se.StreamId,
          se.Event,
          se.Metadata))
        .FirstOrDefault();

      if (nextEvent is null)
      {
        // semaphore.Release();
        await Task.Delay(5, cancellationToken);
        continue;
      }

      currentGlobalPosition = nextEvent.Metadata.GlobalPosition;

      if (hasSwimlanes && !lanes.Contains(nextEvent.Swimlane))
      {
        // semaphore.Release();
        continue;
      }

      yield return nextEvent;
      // semaphore.Release();
    }
    // ReSharper disable once IteratorNeverReturns
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Subscribe(
    SubscribeStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    long? currentStreamPosition = request.IsFromStart
      ? null
      : events
        .Where(se => se.StreamId == request.Id)
        .Where(se => se.Swimlane == request.Swimlane)
        .Select(se => se.Metadata.StreamPosition)
        .LastOrDefault();

    var hasStarted = false;

    while (true)
    {
      // await semaphore.WaitAsync(cancellationToken);

      if (!hasStarted)
      {
        yield return new ReadStreamMessage<EventInterface>.ReadingStarted();
        hasStarted = true;
      }

      var nextEvent = events
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
        // semaphore.Release();
        await Task.Delay(5, cancellationToken);
        continue;
      }

      currentStreamPosition = nextEvent.Metadata.StreamPosition;
      yield return nextEvent;
      // semaphore.Release();
    }
    // ReSharper disable once IteratorNeverReturns
  }

  public async Task TruncateStream(string swimlane, StrongId id, long truncateBefore)
  {
    await semaphore.WaitAsync();
    try
    {
      events.RemoveAll(e => e.Swimlane == swimlane && e.StreamId == id && e.Metadata.StreamPosition < truncateBefore);
    }
    finally
    {
      semaphore.Release();
    }
  }

  private record StoredEvent(string Swimlane, StrongId StreamId, EventInterface Event, StoredEventMetadata Metadata);
}
