namespace Nvx.ConsistentAPI.Store.Store;

public record Insertion<EventInterface>(EventInterface Evt, Guid Id);

public record InsertionPayload<EventInterface>(
  string Swimlane,
  StrongId StreamId,
  ulong? InsertAfter,
  (EventInterface Event, EventInsertionMetadataPayload Metadata)[] Insertions)
{
  public InsertionPayload(
    string swimlane,
    StrongId StreamId,
    ulong? insertAfter,
    string? emittedBy,
    Guid? correlationId,
    Guid? causationId,
    Insertion<EventInterface>[] insertions) : this(
    swimlane,
    StreamId,
    insertAfter,
    insertions
      .Select(ins => (ins.Evt,
        new EventInsertionMetadataPayload(
          ins.Id,
          emittedBy,
          correlationId ?? Guid.NewGuid(),
          causationId,
          DateTime.UtcNow)))
      .ToArray()
  ) { }

  public InsertionPayload(
    string swimlane,
    StrongId StreamId,
    ulong? insertAfter,
    string? emittedBy,
    Guid? correlationId,
    Guid? causationId,
    EventInterface[] events) : this(
    swimlane,
    StreamId,
    insertAfter,
    emittedBy,
    correlationId,
    causationId,
    events.Select(e => new Insertion<EventInterface>(e, Guid.NewGuid())).ToArray()) { }

  public InsertionPayload(string swimlane, StrongId StreamId, EventInterface[] events)
    : this(swimlane, StreamId, null, null, null, null, events) { }
}
