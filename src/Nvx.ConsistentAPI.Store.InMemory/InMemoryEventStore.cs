using System.Runtime.CompilerServices;
using DeFuncto;
using Nvx.ConsistentAPI.Store.Events;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI.Store.InMemory;

public class InMemoryEventStore<EventInterface> : EventStore<EventInterface>
  where EventInterface : HasSwimlane, HasEntityId
{
  private const int DelayPollingSubscriptions = 25;
  private const int TimeoutForCaughtUp = 2_500;
  private const int TimesUntilCaughtUp = TimeoutForCaughtUp / DelayPollingSubscriptions;
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
        SafeRelease();
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

      SafeRelease();
      return new InsertionSuccess(
        events.Select(e => e.Metadata.GlobalPosition).LastOrDefault(),
        events.Select(e => e.Metadata.StreamPosition).LastOrDefault());
    }
    catch
    {
      SafeRelease();
      return new InsertionFailure.InsertionFailed();
    }
  }

  public async IAsyncEnumerable<ReadAllMessage<EventInterface>> Read(
    ReadAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    yield return new ReadAllMessage<EventInterface>.ReadingStarted();
    await semaphore.WaitAsync(cancellationToken);
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
    SafeRelease();

    var lanes = request.Swimlanes ?? [];
    var hasSwimlanes = lanes.Length > 0;

    while (index != edge)
    {
      await semaphore.WaitAsync(cancellationToken);
      var storedEvent = events.FirstOrDefault(e => e.Metadata.GlobalPosition == index);
      SafeRelease();
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

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Read(
    ReadStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    yield return new ReadStreamMessage<EventInterface>.ReadingStarted();

    await semaphore.WaitAsync(cancellationToken);
    var streamEvents = events
      .Where(se => se.StreamId == request.Id)
      .Where(se => se.Swimlane == request.Swimlane)
      .ToArray();
    SafeRelease();

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

  public async IAsyncEnumerable<ReadAllMessage<EventInterface>> Subscribe(
    SubscribeAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ulong timesWithoutNewEvents = 0;
    var emittedFellBehind = false;
    var currentGlobalPosition = 0UL;
    try
    {
      await semaphore.WaitAsync(cancellationToken);
      currentGlobalPosition = request.Position ?? events.Select(se => se.Metadata.GlobalPosition).LastOrDefault();
    }
    catch
    {
      // ignore
    }

    SafeRelease();

    var lanes = request.Swimlanes ?? [];
    var hasSwimlanes = lanes.Length > 0;
    var hasStarted = false;
    while (true)
    {
      if (!hasStarted)
      {
        yield return new ReadAllMessage<EventInterface>.ReadingStarted();
        hasStarted = true;
      }

      StoredEvent[] nextEvents = [];
      try
      {
        await semaphore.WaitAsync(cancellationToken);
        nextEvents = events
          .Where(se => se.Metadata.GlobalPosition > currentGlobalPosition)
          .ToArray();
      }
      catch
      {
        // ignored
      }

      SafeRelease();

      var nextEvent = nextEvents
        .Take(1)
        .Select(se => new ReadAllMessage<EventInterface>.AllEvent(
          se.Swimlane,
          se.StreamId,
          se.Event,
          se.Metadata))
        .FirstOrDefault();

      if (nextEvent is null)
      {
        timesWithoutNewEvents++;
        emittedFellBehind = false;
        if (timesWithoutNewEvents == TimesUntilCaughtUp)
        {
          yield return new ReadAllMessage<EventInterface>.CaughtUp();
        }

        await Task.Delay(DelayPollingSubscriptions, cancellationToken);
        continue;
      }

      if (nextEvents.Length > 1 && !emittedFellBehind)
      {
        emittedFellBehind = true;
        timesWithoutNewEvents = 0;
        yield return new ReadAllMessage<EventInterface>.FellBehind();
      }

      currentGlobalPosition = nextEvent.Metadata.GlobalPosition;

      if (hasSwimlanes && !lanes.Contains(nextEvent.Swimlane))
      {
        continue;
      }

      yield return nextEvent;
    }
    // ReSharper disable once IteratorNeverReturns
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Subscribe(
    SubscribeStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await semaphore.WaitAsync(cancellationToken);
    ulong timesWithoutNewEvents = 0;
    var emittedFellBehind = false;
    long? currentStreamPosition = request.IsFromStart
      ? null
      : events
        .Where(se => se.StreamId == request.Id)
        .Where(se => se.Swimlane == request.Swimlane)
        .Select(se => se.Metadata.StreamPosition)
        .LastOrDefault();
    SafeRelease();

    var hasStarted = false;

    while (true)
    {
      if (!hasStarted)
      {
        yield return new ReadStreamMessage<EventInterface>.ReadingStarted();
        hasStarted = true;
      }

      await semaphore.WaitAsync(cancellationToken);
      var nextEvents = events
        .Where(se => se.StreamId == request.Id)
        .Where(se => se.Swimlane == request.Swimlane)
        .Where(se => se.Metadata.StreamPosition > currentStreamPosition || currentStreamPosition == null)
        .Select(se => new ReadStreamMessage<EventInterface>.SolvedEvent(
          se.Swimlane,
          se.StreamId,
          se.Event,
          se.Metadata))
        .ToArray();
      SafeRelease();

      var nextEvent = nextEvents.FirstOrDefault();

      if (nextEvent is null)
      {
        timesWithoutNewEvents++;
        emittedFellBehind = false;
        if (timesWithoutNewEvents == TimesUntilCaughtUp)
        {
          yield return new ReadStreamMessage<EventInterface>.CaughtUp();
        }

        await Task.Delay(DelayPollingSubscriptions, cancellationToken);
        continue;
      }

      if (nextEvents.Length > 1 && !emittedFellBehind)
      {
        emittedFellBehind = true;
        timesWithoutNewEvents = 0;
        yield return new ReadStreamMessage<EventInterface>.FellBehind();
      }

      currentStreamPosition = nextEvent.Metadata.StreamPosition;
      yield return nextEvent;
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
      SafeRelease();
    }
  }

  private void SafeRelease()
  {
    try
    {
      semaphore.Release();
    }
    catch
    {
      //ignore
    }
  }

  private record StoredEvent(string Swimlane, StrongId StreamId, EventInterface Event, StoredEventMetadata Metadata);
}
