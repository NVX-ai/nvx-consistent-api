namespace Nvx.ConsistentAPI.EventModeling;

public interface Endpoint
{
  /// <summary>
  ///   Authentication options:
  ///   - Everyone: Everyone is allowed, for tenant bound commands, it overriden by `EveryoneAuthenticated`.
  ///   - EveryoneAuthenticated: Every authenticated user is allowed, still applies tenancy constraints.
  ///   - PermissionsRequireAll: Only users with all permissions referenced in the constructor are allowed.
  ///   - PermissionsRequireOne: Users with at least one of the permissions referenced in the constructor are allowed.
  /// </summary>
  AuthOptions Auth { get; }
}
