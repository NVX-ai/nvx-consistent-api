namespace Nvx.ConsistentAPI;

public record AddToTenant(string Sub) : TenantEventModelCommand<UserSecurity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(Sub);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<UserSecurity> entity,
    UserSecurity user,
    FileUpload[] files) => new AnyState(new AddedToTenant(Sub, tenantId));
}
