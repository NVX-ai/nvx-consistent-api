namespace Nvx.ConsistentAPI;

public record AddedToTenant(string Sub, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => UserSecurity.GetStreamName(Sub);
  public StrongId GetEntityId() => new StrongString(Sub);
}
