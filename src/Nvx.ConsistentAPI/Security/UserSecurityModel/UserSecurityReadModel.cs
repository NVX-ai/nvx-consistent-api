namespace Nvx.ConsistentAPI;

public record UserSecurityReadModel(
  string Id,
  string Sub,
  string Email,
  string FullName,
  Dictionary<Guid, UserSecurity.ReceivedRole[]> TenantRoles,
  string[] ApplicationPermissions,
  Dictionary<Guid, string[]> TenantPermissions,
  TenantDetails[] Tenants) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongString(Id);
}
