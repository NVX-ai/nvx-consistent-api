namespace Nvx.ConsistentAPI.Store.Store;

public record Insertion<EventInterface>(EventInterface Evt, Guid Id);

public record InsertionPayload<EventInterface>(
  string Swimlane,
  StrongId StrongId,
  ulong? InsertAfter,
  (EventInterface Event, EventInsertionMetadataPayload Metadata)[] Insertions)
{
  public InsertionPayload(
    string swimlane,
    StrongId StrongId,
    ulong? insertAfter,
    string? emittedBy,
    Guid? correlationId,
    Guid? causationId,
    Insertion<EventInterface>[] insertions) : this(
    swimlane,
    StrongId,
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
    StrongId StrongId,
    ulong? insertAfter,
    string? emittedBy,
    Guid? correlationId,
    Guid? causationId,
    EventInterface[] events) : this(
    swimlane,
    StrongId,
    insertAfter,
    emittedBy,
    correlationId,
    causationId,
    events.Select(e => new Insertion<EventInterface>(e, Guid.NewGuid())).ToArray()) { }

  public InsertionPayload(string swimlane, StrongId StrongId, EventInterface[] events)
    : this(swimlane, StrongId, null, null, null, null, events) { }
}
