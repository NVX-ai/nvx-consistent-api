namespace Nvx.ConsistentAPI.Store.Store;

public record StoredEventMetadata(
  Guid EventId,
  string? RelatedUserSub,
  string? CorrelationId,
  string? CausationId,
  DateTime CreatedAt,
  ulong GlobalPosition,
  long StreamPosition)
{
  public static StoredEventMetadata FromStorage(
    EventMetadata metadata,
    Guid eventId,
    ulong globalPosition,
    long streamPosition) =>
    new(
      eventId,
      metadata.RelatedUserSub,
      metadata.CorrelationId,
      metadata.CausationId,
      metadata.CreatedAt,
      globalPosition,
      streamPosition);
}
