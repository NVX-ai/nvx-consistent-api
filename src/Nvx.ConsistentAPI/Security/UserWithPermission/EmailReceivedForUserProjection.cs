namespace Nvx.ConsistentAPI;

public record EmailReceivedForUserProjection(string Sub, string Permission, string Email) : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithPermissionId(Sub, Permission);
  public string GetStreamName() => $"{UserWithPermissionProjection.StreamPrefix}{GetEntityId()}";
}
