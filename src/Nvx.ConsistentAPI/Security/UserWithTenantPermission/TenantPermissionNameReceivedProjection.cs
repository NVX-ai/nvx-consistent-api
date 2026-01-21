namespace Nvx.ConsistentAPI;

public record TenantPermissionNameReceivedProjection(string Sub, Guid TenantId, string Permission, string Name)
  : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithTenantPermissionId(Sub, TenantId, Permission);
  public string GetStreamName() => $"{UserWithTenantPermissionProjection.StreamPrefix}{GetEntityId()}";
}
