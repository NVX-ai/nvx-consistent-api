using KurrentDB.Client;
using Nvx.ConsistentAPI.Framework.Serialization;

namespace Nvx.ConsistentAPI.Framework.Events;

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
