namespace Nvx.ConsistentAPI.EventStore.Store;

public record StoredEventMetadata(
  Guid EventId,
  string? EmittedBy,
  Guid CorrelationId,
  Guid? CausationId,
  DateTime EmittedAt,
  ulong GlobalPosition,
  ulong StreamPosition);
