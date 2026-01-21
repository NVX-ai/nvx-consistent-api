namespace Nvx.ConsistentAPI.Security;

public delegate bool CustomAuthorization<EntityShape, CommandShape>(
  Option<UserSecurity> user,
  Dictionary<string, string> headers,
  Dictionary<string, string> queryParams,
  Option<EntityShape> entity,
  CommandShape command);
