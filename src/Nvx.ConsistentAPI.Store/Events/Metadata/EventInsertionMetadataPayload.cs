namespace Nvx.ConsistentAPI.Store.Store;

public record EventInsertionMetadataPayload(
  Guid EventId,
  string? RelatedUserSub,
  string? CorrelationId,
  string? CausationId,
  DateTime CreatedAt);
