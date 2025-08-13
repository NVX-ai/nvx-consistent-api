using Nvx.ConsistentAPI.Store.Events;

namespace Nvx.ConsistentAPI;

public record EventMetadata(
  DateTime CreatedAt,
  string? CorrelationId,
  string? CausationId,
  string? RelatedUserSub,
  ulong? GlobalPosition,
  long? StreamPosition)
{
  public byte[] ToBytes() => EventSerialization.ToBytes(this);

  public static EventMetadata TryParse(byte[] bytes, DateTime createdAt, ulong globalPosition, long streamPosition)
  {
    try
    {
      var deserialized = EventSerialization.Deserialize<EventMetadata>(bytes);
      return deserialized is null
        ? new EventMetadata(createdAt, null, null, null, globalPosition, streamPosition)
        : deserialized with { GlobalPosition = globalPosition, StreamPosition = streamPosition };
    }
    catch
    {
      return new EventMetadata(createdAt, null, null, null, globalPosition, streamPosition);
    }
  }
}
