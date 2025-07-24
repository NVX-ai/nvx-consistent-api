namespace Nvx.ConsistentAPI.Store.Store;

public record Insertion<EventInterface>(EventInterface Evt, Guid Id);

public interface InsertionType;

public record StreamCreation : InsertionType;

public record InsertAfter(ulong Position) : InsertionType;

public record ExistingStream : InsertionType;

public record AnyStreamState : InsertionType;

public record InsertionPayload<EventInterface>(
  string Swimlane,
  StrongId StreamId,
  InsertionType InsertionType,
  (EventInterface Event, EventInsertionMetadataPayload Metadata)[] Insertions)
{
  public InsertionPayload(
    string swimlane,
    StrongId StreamId,
    InsertionType insertionType,
    string? emittedBy,
    string? correlationId,
    string? causationId,
    Insertion<EventInterface>[] insertions) : this(
    swimlane,
    StreamId,
    insertionType,
    insertions
      .Select(ins => (ins.Evt,
        new EventInsertionMetadataPayload(
          ins.Id,
          emittedBy,
          correlationId ?? Guid.NewGuid().ToString(),
          causationId,
          DateTime.UtcNow)))
      .ToArray()
  ) { }

  public InsertionPayload(
    string swimlane,
    StrongId StreamId,
    InsertionType insertionType,
    string? emittedBy,
    string? correlationId,
    string? causationId,
    EventInterface[] events) : this(
    swimlane,
    StreamId,
    insertionType,
    emittedBy,
    correlationId,
    causationId,
    events.Select(e => new Insertion<EventInterface>(e, Guid.NewGuid())).ToArray()) { }

  public InsertionPayload(string swimlane, StrongId StreamId, EventInterface[] events)
    : this(swimlane, StreamId, new AnyStreamState(), null, null, null, events) { }
}
