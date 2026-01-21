namespace Nvx.ConsistentAPI;

public record RevokeApplicationPermission(string Sub, string Permission) : EventModelCommand<UserSecurity>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Sub);

  public Result<EventInsertion, ApiError> Decide(
    Option<UserSecurity> us,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new AnyState(new ApplicationPermissionRevoked(Sub, Permission));
}
