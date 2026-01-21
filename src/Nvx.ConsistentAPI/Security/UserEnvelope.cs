namespace Nvx.ConsistentAPI.Security;

public record UserEnvelope(string SubjectId, string? Email, string? Name);
