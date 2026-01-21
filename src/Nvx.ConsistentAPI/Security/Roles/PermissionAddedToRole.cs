namespace Nvx.ConsistentAPI;

public record PermissionAddedToRole(Guid Id, string Permission, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => RoleEntity.GetStreamName(new RoleId(Id, TenantId));
  public StrongId GetEntityId() => new RoleId(Id, TenantId);
}
