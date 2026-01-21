namespace Nvx.ConsistentAPI;

public record UserWithTenantPermissionId(string Sub, Guid TenantId, string Permission) : StrongId
{
  public override string StreamId() => $"{Sub}#{TenantId}#{Permission}";
  public override string ToString() => StreamId();
}
