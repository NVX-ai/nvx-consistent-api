namespace Nvx.ConsistentAPI;

public record TenantRoleAssigned(string Sub, Guid RoleId, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => UserSecurity.GetStreamName(Sub);
  public StrongId GetEntityId() => new StrongString(Sub);
}
