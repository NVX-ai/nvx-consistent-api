namespace Nvx.ConsistentAPI.EventStore.Store;

public record EventInsertionMetadataPayload(
  Guid EventId,
  string? EmittedBy,
  Guid CorrelationId,
  Guid? CausationId,
  DateTime EmittedAt);
