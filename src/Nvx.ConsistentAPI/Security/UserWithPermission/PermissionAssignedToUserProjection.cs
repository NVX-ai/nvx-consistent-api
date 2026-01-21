namespace Nvx.ConsistentAPI;

public record PermissionAssignedToUserProjection(string Sub, string Name, string Email, string Permission)
  : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithPermissionId(Sub, Permission);
  public string GetStreamName() => $"{UserWithPermissionProjection.StreamPrefix}{GetEntityId()}";
}
