namespace Nvx.ConsistentAPI;

public record PermissionRemovedFromUserProjection(string Sub, string Permission) : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithPermissionId(Sub, Permission);
  public string GetStreamName() => $"{UserWithPermissionProjection.StreamPrefix}{GetEntityId()}";
}
