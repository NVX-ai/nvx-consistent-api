namespace Nvx.ConsistentAPI;

public record CreateRole(string Name, string Description) : TenantEventModelCommand<RoleEntity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new RoleId(Guid.NewGuid(), tenantId);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<RoleEntity> entity,
    UserSecurity user,
    FileUpload[] files) =>
    this.ShouldCreate(entity, () => new RoleCreated(Guid.NewGuid(), Name, Description, tenantId).ToEventArray());
}
