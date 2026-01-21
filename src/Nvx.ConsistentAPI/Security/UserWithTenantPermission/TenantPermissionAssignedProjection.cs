namespace Nvx.ConsistentAPI;

public record TenantPermissionAssignedProjection(
  string Sub,
  Guid TenantId,
  string Name,
  string Email,
  string Permission)
  : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithTenantPermissionId(Sub, TenantId, Permission);
  public string GetStreamName() => $"{UserWithTenantPermissionProjection.StreamPrefix}{GetEntityId()}";
}
