namespace Nvx.ConsistentAPI;

public record DescribeTemplateUserRole(Guid? TemplateUserRoleId, string Name, string Description)
  : EventModelCommand<TemplateUserRoleEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<TemplateUserRoleEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) =>
    TemplateUserRoleId.HasValue
      ? this.Require(
        entity,
        _ => new ExistingStream(
          new TemplateUserRoleUpdated(TemplateUserRoleId.Value, Name, Description)))
      : this.ShouldCreate(
        entity,
        () => new EventModelEvent[]
          { new TemplateUserRoleDescribed(Guid.NewGuid(), Name, Description) });

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) =>
    Optional(TemplateUserRoleId).Map(StrongId (id) => new TemplateUserRoleId(id));
}

public record RemoveTemplateUserRole(Guid TemplateUserRoleId)
  : EventModelCommand<TemplateUserRoleEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<TemplateUserRoleEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) =>
    this.Require(entity, _ => new ExistingStream(new TemplateUserRoleRemoved(TemplateUserRoleId)));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new TemplateUserRoleId(TemplateUserRoleId);
}

public record AddTemplateUserRolePermission(Guid TemplateUserRoleId, string Permission)
  : EventModelCommand<TemplateUserRoleEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<TemplateUserRoleEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) =>
    this.Require(entity, _ => new ExistingStream(new TemplateUserRolePermissionAdded(TemplateUserRoleId, Permission)));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new TemplateUserRoleId(TemplateUserRoleId);
}

public record RemoveTemplateUserRolePermission(Guid TemplateUserRoleId, string Permission)
  : EventModelCommand<TemplateUserRoleEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<TemplateUserRoleEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) =>
    this.Require(
      entity,
      _ => new ExistingStream(new TemplateUserRolePermissionRemoved(TemplateUserRoleId, Permission)));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new TemplateUserRoleId(TemplateUserRoleId);
}
