namespace Nvx.ConsistentAPI;

public record ApplicationPermissionRevoked(string Sub, string Permission) : EventModelEvent
{
  public string GetStreamName() => UserSecurity.GetStreamName(Sub);
  public StrongId GetEntityId() => new StrongString(Sub);
}
