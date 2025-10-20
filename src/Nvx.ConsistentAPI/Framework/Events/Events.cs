using EventStore.Client;
using Nvx.ConsistentAPI.Framework;

namespace Nvx.ConsistentAPI;

public record EventMetadata(
  DateTime CreatedAt,
  string? CorrelationId,
  string? CausationId,
  string? RelatedUserSub,
  Position? Position)
{
  public byte[] ToBytes() => EventSerialization.ToBytes(this);

  public static EventMetadata TryParse(ResolvedEvent re)
  {
    try
    {
      var deserialized = EventSerialization.Deserialize<EventMetadata>(re.Event.Metadata.ToArray());
      return deserialized is not null
        ? deserialized with { Position = re.OriginalEvent.Position }
        : new EventMetadata(re.Event.Created, null, null, null, re.OriginalEvent.Position);
    }
    catch
    {
      return new EventMetadata(re.Event.Created, null, null, null, re.OriginalEvent.Position);
    }
  }
}

public interface EventModelSnapshotEvent : EventModelEvent;

public interface EventModelEvent
{
  public string EventType => GetType().Apply(Naming.ToSpinalCase);
  string GetStreamName();
  public byte[] ToBytes() => EventSerialization.ToBytes(this);
  StrongId GetEntityId();
  public bool ShouldTriggerHydration(EventMetadata metadata, bool isBackwards) => true;
}

public interface EventInsertion
{
  EventInsertion WithRevision(long revision);
}

public interface ExistingStreamInsertion : EventInsertion;

public record AnyState(params EventModelEvent[] Events) : ExistingStreamInsertion
{
  public EventInsertion WithRevision(long revision) => this;
}

public record CreateStream(params EventModelEvent[] Events) : EventInsertion
{
  public EventInsertion WithRevision(long revision) => this;
}

public record ExistingStream(long ExpectedRevision, params EventModelEvent[] Events) : ExistingStreamInsertion
{
  public ExistingStream(params EventModelEvent[] Events) : this(-1, Events) { }

  public EventInsertion WithRevision(long revision) => this with { ExpectedRevision = revision };
}

/// <summary>
///   Insert events to different streams.
///   <remarks>
///     IT IS NOT TRANSACTIONAL.
///     It will try to insert every event independently, so some might not be inserted.
///   </remarks>
/// </summary>
/// <param name="Events"></param>
public record MultiStream(params EventModelEvent[] Events) : EventInsertion
{
  public EventInsertion WithRevision(long revision) => this;
}

public static class ParserBuilder
{
  public static (string, Func<ResolvedEvent, Option<EventModelEvent>>) Build(Type eventType)
  {
    return (Naming.ToSpinalCase(eventType), Parse);

    Option<EventModelEvent> Parse(ResolvedEvent re)
    {
      try
      {
        return EventSerialization
          .Deserialize(re.Event.Data, eventType)
          .Apply(Optional)
          .Map(o => (EventModelEvent)o);
      }
      catch
      {
        return None;
      }
    }
  }
}
