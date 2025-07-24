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
    semaphore.Wait();
    var latestGlobalPosition = events.Select(se => se.Metadata.GlobalPosition).LastOrDefault();
    var latestStreamPosition = events
      .Where(se => se.StreamId == payload.StreamId)
      .Where(se => se.Swimlane == payload.Swimlane)
      .Select(se => se.Metadata.StreamPosition)
      .LastOrDefault();

    if ((payload.InsertionType is StreamCreation && latestStreamPosition != 0)
        || (payload.InsertionType is ExistingStream && latestStreamPosition == 0)
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
      from tuple in nonExistingEvents.Select((t, i) => (t, i: (ulong)i))
      let evt = tuple.t.Event
      let md = tuple.t.Metadata
      let index = tuple.i
      select new StoredEvent(
        payload.Swimlane,
        payload.StreamId,
        evt,
        new StoredEventMetadata(
          md.EventId,
          md.EmittedBy,
          md.CorrelationId,
          md.CausationId,
          md.EmittedAt,
          latestGlobalPosition + index + 1,
          latestStreamPosition + index + 1)));

    var success = new InsertionSuccess(
      events.Select(e => e.Metadata.GlobalPosition).LastOrDefault(),
      events.Select(e => e.Metadata.StreamPosition).LastOrDefault());
    semaphore.Release();
    return success;
  }

  public async IAsyncEnumerable<ReadAllMessage> Read(
    ReadAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await semaphore.WaitAsync(cancellationToken);
    yield return new ReadAllMessage.ReadingStarted();
    ulong? index = request.Relative switch
    {
      RelativePosition.Start => 0,
      RelativePosition.End => (ulong)events.Count,
      _ => request.Direction switch
      {
        ReadDirection.Backwards => request.Position - 1,
        _ => request.Position + 1
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

      yield return new ReadAllMessage.AllEvent(
        storedEvent.Swimlane,
        storedEvent.StreamId,
        storedEvent.Metadata);
    }

    semaphore.Release();
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Read(
    ReadStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await semaphore.WaitAsync(cancellationToken);
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
               && storedEvent.Metadata.StreamPosition >= request.StreamPosition:
        case ReadDirection.Forwards
          when request.StreamPosition.HasValue
               && storedEvent.Metadata.StreamPosition <= request.StreamPosition:
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

    semaphore.Release();
  }

  public async IAsyncEnumerable<ReadAllMessage> Subscribe(
    SubscribeAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var currentGlobalPosition = request.Position ?? events.Select(se => se.Metadata.GlobalPosition).LastOrDefault();
    var lanes = request.Swimlanes ?? [];
    var hasSwimlanes = lanes.Length > 0;
    var hasStarted = false;
    while (true)
    {
      await semaphore.WaitAsync(cancellationToken);
      if (!hasStarted)
      {
        yield return new ReadAllMessage.ReadingStarted();
        hasStarted = true;
      }

      var nextEvent = events
        .Where(se => se.Metadata.GlobalPosition > currentGlobalPosition)
        .Select(se => new ReadAllMessage.AllEvent(
          se.Swimlane,
          se.StreamId,
          se.Metadata))
        .FirstOrDefault();

      if (nextEvent is null)
      {
        semaphore.Release();
        await Task.Delay(5, cancellationToken);
        continue;
      }

      currentGlobalPosition = nextEvent.Metadata.GlobalPosition;

      if (hasSwimlanes && !lanes.Contains(nextEvent.Swimlane))
      {
        semaphore.Release();
        continue;
      }

      yield return nextEvent;
      semaphore.Release();
    }
    // ReSharper disable once IteratorNeverReturns
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Subscribe(
    SubscribeStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ulong? currentStreamPosition = request.IsFromStart
      ? null
      : events
        .Where(se => se.StreamId == request.Id)
        .Where(se => se.Swimlane == request.Swimlane)
        .Select(se => se.Metadata.StreamPosition)
        .LastOrDefault();

    var hasStarted = false;

    while (true)
    {
      await semaphore.WaitAsync(cancellationToken);
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
        semaphore.Release();
        await Task.Delay(5, cancellationToken);
        continue;
      }

      currentStreamPosition = nextEvent.Metadata.StreamPosition;
      yield return nextEvent;
      semaphore.Release();
    }
    // ReSharper disable once IteratorNeverReturns
  }

  public async Task TruncateStream(StrongId id, ulong truncateBefore)
  {
    await semaphore.WaitAsync();
    try
    {
      events.RemoveAll(e => e.StreamId == id && e.Metadata.StreamPosition < truncateBefore);
    }
    finally
    {
      semaphore.Release();
    }
  }

  private record StoredEvent(string Swimlane, StrongId StreamId, EventInterface Event, StoredEventMetadata Metadata);
}
