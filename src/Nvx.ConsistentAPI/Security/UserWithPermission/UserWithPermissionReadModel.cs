namespace Nvx.ConsistentAPI;

public record UserWithPermissionReadModel(string Sub, string? Name, string? Email, string Permission, string Id)
  : EventModelReadModel
{
  public StrongId GetStrongId() => new UserWithPermissionId(Sub, Permission);
}
