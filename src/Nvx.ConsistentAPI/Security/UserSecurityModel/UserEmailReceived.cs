namespace Nvx.ConsistentAPI;

public record UserEmailReceived(string Sub, string Email) : EventModelEvent
{
  public string GetStreamName() => UserSecurity.GetStreamName(Sub);
  public StrongId GetEntityId() => new StrongString(Sub);
}
