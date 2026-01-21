namespace Nvx.ConsistentAPI;

public record PermissionRemovedFromRole(Guid Id, string Permission, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => RoleEntity.GetStreamName(new RoleId(Id, TenantId));
  public StrongId GetEntityId() => new RoleId(Id, TenantId);
}
