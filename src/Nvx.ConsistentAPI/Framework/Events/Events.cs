using EventStore.Client;
using Nvx.ConsistentAPI.Store.Events;

namespace Nvx.ConsistentAPI;

public interface EventModelSnapshotEvent : EventModelEvent;

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
