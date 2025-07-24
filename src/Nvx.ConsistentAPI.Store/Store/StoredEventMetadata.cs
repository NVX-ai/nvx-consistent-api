namespace Nvx.ConsistentAPI.Store.Store;

public record StoredEventMetadata(
  Guid EventId,
  string? EmittedBy,
  Guid CorrelationId,
  Guid? CausationId,
  DateTime EmittedAt,
  ulong GlobalPosition,
  ulong StreamPosition);
