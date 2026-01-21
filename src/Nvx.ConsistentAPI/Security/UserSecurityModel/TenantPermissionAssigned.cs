namespace Nvx.ConsistentAPI;

public record TenantPermissionAssigned(string Sub, string Permission, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => UserSecurity.GetStreamName(Sub);
  public StrongId GetEntityId() => new StrongString(Sub);
}
