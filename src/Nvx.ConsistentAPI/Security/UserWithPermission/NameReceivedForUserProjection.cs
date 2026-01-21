namespace Nvx.ConsistentAPI;

public record NameReceivedForUserProjection(string Sub, string Permission, string Name) : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithPermissionId(Sub, Permission);
  public string GetStreamName() => $"{UserWithPermissionProjection.StreamPrefix}{GetEntityId()}";
}
