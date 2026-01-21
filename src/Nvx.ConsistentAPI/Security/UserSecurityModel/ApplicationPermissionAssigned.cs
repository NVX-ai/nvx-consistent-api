namespace Nvx.ConsistentAPI;

public record ApplicationPermissionAssigned(string Sub, string Permission) : EventModelEvent
{
  public string GetStreamName() => UserSecurity.GetStreamName(Sub);
  public StrongId GetEntityId() => new StrongString(Sub);
}
