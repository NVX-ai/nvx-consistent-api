namespace Nvx.ConsistentAPI;

public record TenantRoleRevoked(string Sub, Guid RoleId, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => UserSecurity.GetStreamName(Sub);
  public StrongId GetEntityId() => new StrongString(Sub);
}
