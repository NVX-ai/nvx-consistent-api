namespace Nvx.ConsistentAPI;

public record TenantPermissionEmailReceivedProjection(string Sub, Guid TenantId, string Permission, string Email)
  : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithTenantPermissionId(Sub, TenantId, Permission);
  public string GetStreamName() => $"{UserWithTenantPermissionProjection.StreamPrefix}{GetEntityId()}";
}
