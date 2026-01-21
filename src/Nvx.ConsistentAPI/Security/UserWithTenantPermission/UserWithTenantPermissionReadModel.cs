namespace Nvx.ConsistentAPI;

public record UserWithTenantPermissionReadModel(
  string Sub,
  Guid TenantId,
  string? Name,
  string? Email,
  string Permission,
  string Id)
  : EventModelReadModel
{
  public StrongId GetStrongId() => new UserWithTenantPermissionId(Sub, TenantId, Permission);
}
