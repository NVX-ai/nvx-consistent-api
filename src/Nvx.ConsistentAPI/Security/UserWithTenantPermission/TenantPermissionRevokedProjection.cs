namespace Nvx.ConsistentAPI;

public record TenantPermissionRevokedProjection(string Sub, Guid TenantId, string Permission)
  : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithTenantPermissionId(Sub, TenantId, Permission);
  public string GetStreamName() => $"{UserWithTenantPermissionProjection.StreamPrefix}{GetEntityId()}";
}
