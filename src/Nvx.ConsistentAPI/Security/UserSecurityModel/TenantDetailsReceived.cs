namespace Nvx.ConsistentAPI;

public record TenantDetailsReceived(string Sub, Guid TenantId, string TenantName) : EventModelEvent
{
  public string GetStreamName() => UserSecurity.GetStreamName(Sub);
  public StrongId GetEntityId() => new StrongString(Sub);
}
