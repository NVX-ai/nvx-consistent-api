namespace Nvx.ConsistentAPI;

public record UserNameReceived(string Sub, string FullName) : EventModelEvent
{
  public string GetStreamName() => UserSecurity.GetStreamName(Sub);
  public StrongId GetEntityId() => new StrongString(Sub);
}
