namespace Nvx.ConsistentAPI;

public record RoleReadModel(
  string Id,
  Guid RoleId,
  string Name,
  string Description,
  string[] Permissions,
  Guid TenantId)
  : EventModelReadModel, IsTenantBound
{
  public StrongId GetStrongId() => new RoleId(RoleId, TenantId);
}
