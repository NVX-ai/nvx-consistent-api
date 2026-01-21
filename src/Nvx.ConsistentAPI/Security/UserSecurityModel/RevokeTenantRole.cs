namespace Nvx.ConsistentAPI;

public record RevokeTenantRole(string UserSub, Guid RoleId) : TenantEventModelCommand<UserSecurity>
{
  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<UserSecurity> entity,
    UserSecurity user,
    FileUpload[] files) =>
    new AnyState(new TenantRoleRevoked(UserSub, RoleId, tenantId));

  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(UserSub);
}
