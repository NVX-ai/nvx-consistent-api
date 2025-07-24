namespace Nvx.ConsistentAPI;

public record EventContext(string? CorrelationId, string? CausationId, string? RelatedUserSub);
