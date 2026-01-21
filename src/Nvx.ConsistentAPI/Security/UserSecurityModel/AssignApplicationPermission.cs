namespace Nvx.ConsistentAPI;

public record AssignApplicationPermission(string Sub, string Permission) : EventModelCommand<UserSecurity>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Sub);

  public Result<EventInsertion, ApiError> Decide(
    Option<UserSecurity> us,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new AnyState(new ApplicationPermissionAssigned(Sub, Permission));
}
