namespace Nvx.ConsistentAPI;

public record RemovePermissionFromRole(Guid Id, string Permission) : TenantEventModelCommand<RoleEntity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new RoleId(Id, tenantId);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<RoleEntity> entity,
    UserSecurity user,
    FileUpload[] files) => this.Require(
    entity,
    user,
    tenantId,
    _ => new ExistingStream(new PermissionRemovedFromRole(Id, Permission, tenantId)));
}
