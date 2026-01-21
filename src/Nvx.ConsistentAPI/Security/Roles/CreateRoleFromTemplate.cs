namespace Nvx.ConsistentAPI;

public record CreateRoleFromTemplate(Guid TemplateId) : TenantEventModelCommand<RoleEntity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new RoleId(TemplateId, tenantId);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<RoleEntity> entity,
    UserSecurity user,
    FileUpload[] files) =>
    this.ShouldCreate(entity, () => new RoleCreatedFromTemplate(Guid.NewGuid(), TemplateId, tenantId).ToEventArray());
}
