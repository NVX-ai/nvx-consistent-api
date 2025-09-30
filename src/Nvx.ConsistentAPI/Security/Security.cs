using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Nvx.ConsistentAPI;

public record Everyone;

public record EveryoneAuthenticated;

public record PermissionsRequireAll(params string[] Permissions);

public record PermissionsRequireOne(params string[] Permissions);

public delegate bool CustomAuthorization<EntityShape, CommandShape>(
  Option<UserSecurity> user,
  Dictionary<string, string> headers,
  Dictionary<string, string> queryParams,
  Option<EntityShape> entity,
  CommandShape command);

public record UserEnvelope(string SubjectId, string? Email, string? Name);

public static class FrameworkSecurity
{
  private static readonly Lazy<JsonWebTokenHandler> TokenHandler = new(() =>
  {
    JsonWebTokenHandler.DefaultMapInboundClaims = true;
    JsonWebTokenHandler.DefaultInboundClaimTypeMap[JwtRegisteredClaimNames.Sub] = ClaimTypes.NameIdentifier;
    return new JsonWebTokenHandler();
  });

  internal static void ApplyAuth(this RouteHandlerBuilder self, AuthOptions auth)
  {
    if (!auth.IsProtected())
    {
      return;
    }

    self
      .Produces<ErrorResponse>(401)
      .Apply(MayBeForbidden)
      .WithOpenApi(o =>
      {
        o.Description = $"**Requires authentication.**\n\n{AuthMessage()}\n\n{o.Description ?? ""}\n\n";
        return o;
      });

    return;

    RouteHandlerBuilder MayBeForbidden(
      RouteHandlerBuilder builder) =>
      auth.Match(
        _ => builder,
        _ => builder,
        _ => builder.Produces<ErrorResponse>(403),
        _ => builder.Produces<ErrorResponse>(403)
      );

    string AuthMessage() =>
      auth.Match(
        _ => "",
        _ => "",
        all => $"User must have **all** of the following permissions: \n{PermissionsList(all.Permissions)}\n",
        one => $"User must have **one** of the following permissions: \n{PermissionsList(one.Permissions)}\n"
      );

    string PermissionsList(IEnumerable<string> permissions) =>
      permissions.Select(r => $"- {r}").Apply(r => string.Join(".\n", r));
  }

  private static AsyncOption<UserEnvelope> TryGetUser(
    HttpContext context,
    SecurityKey[] publicSigningKeys)
  {
    return Go();

    async Task<Option<UserEnvelope>> Go()
    {
      if (!context.Request.Headers.TryGetValue("Authorization", out var hdr))
      {
        return Option<UserEnvelope>.None;
      }

      var header = hdr.ToString();
      if (!header.StartsWith("Bearer "))
      {
        return Option<UserEnvelope>.None;
      }

      var validationParameters = new TokenValidationParameters
      {
        IssuerSigningKeys = publicSigningKeys,
        ValidateAudience = false,
        ValidateIssuerSigningKey = true,
        ValidateIssuer = false
      };

      var token = header["Bearer ".Length..].Trim();
      try
      {
        var claimsPrincipal = await TokenHandler.Value.ValidateTokenAsync(token, validationParameters);

        return Optional(TryGetProperty("sub") ?? TryGetProperty(ClaimTypes.NameIdentifier))
          .Map(sub => new UserEnvelope(
            sub,
            TryGetProperty("emails") ?? TryGetProperty("email") ?? TryGetProperty(ClaimTypes.Email),
            TryGetProperty("preferred_username") ?? TryGetProperty("name") ?? TryGetProperty(ClaimTypes.Name)
          ));

        string? TryGetProperty(string key) =>
          claimsPrincipal.Claims.TryGetValue(key, out var value) ? (string?)value : null;
      }
      catch (Exception)
      {
        return Option<UserEnvelope>.None;
      }
    }
  }

  internal static AsyncResult<Unit, ApiError> Validate(
    AuthOptions auth,
    Option<UserSecurity> user,
    Option<Guid> tenantId
  )
  {
    return user.Map(u => u.Apply(EffectivePermissions(tenantId)).Contains("admin")).DefaultValue(false)
      ? unit
      : auth.Match<AsyncResult<Unit, ApiError>>(
        _ => ByTenant(),
        _ => user.Match<AsyncResult<Unit, ApiError>>(_ => unit, () => new UnauthorizedError()),
        all => user.Match<AsyncResult<Unit, ApiError>>(
          u => all.Permissions.All(u.Apply(EffectivePermissions(tenantId)).Contains) ? unit : new ForbiddenError(),
          () => new UnauthorizedError()),
        oneOf => user.Match<AsyncResult<Unit, ApiError>>(
          u => oneOf.Permissions.Any(u.Apply(EffectivePermissions(tenantId)).Contains) ? unit : new ForbiddenError(),
          () => new UnauthorizedError())
      );

    Result<Unit, ApiError> ByTenant() =>
      tenantId.Match(
        id =>
          user.Match<Result<Unit, ApiError>>(
            u => u.TenantPermissions.ContainsKey(id) ? unit : new ForbiddenError(),
            () => new UnauthorizedError()
          ),
        () => unit
      );
  }

  private static Guid[] AvailableTenants(Option<UserSecurity> maybeUser, AuthOptions auth) =>
    maybeUser
      .Map(userSecurity =>
        auth.Match<Guid[]>(
          _ => userSecurity.ActiveInTenants,
          _ => userSecurity.ActiveInTenants,
          ra => userSecurity
            .TenantPermissions.Where(tp => ra.Permissions.All(p => tp.Value.Contains("admin") || tp.Value.Contains(p)))
            .Select(tp => tp.Key)
            .ToArray(),
          ro => userSecurity
            .TenantPermissions.Where(tp => ro.Permissions.Any(p => tp.Value.Contains("admin") || tp.Value.Contains(p)))
            .Select(tp => tp.Key)
            .ToArray()))
      .DefaultValue([]);


  internal static Func<UserSecurity, string[]> EffectivePermissions(Option<Guid> t) => user =>
    t.Match(
      id =>
        user
          .ApplicationPermissions
          .Concat(
            user.TenantPermissions.TryGetValue(id, out var value)
              ? value
              : []
          )
          .ToArray(),
      () => user.ApplicationPermissions);

  internal static AsyncResult<Option<UserSecurity>, ApiError> Authorization(
    HttpContext context,
    Fetcher fetcher,
    Emitter emitter,
    GeneratorSettings settings,
    AuthOptions auth,
    Option<Guid> tenantId) =>
    from subject in TryGetUser(context, settings.JwtPublicKeys).Apply(Elevate)
    from user in FetchUser(fetcher, subject.Map(ue => new StrongString(ue.SubjectId)))
    from updated in UpdateUser(subject, user, emitter)
    from valid in Validate(auth, user, tenantId)
    select user;

  internal static AsyncResult<Option<(UserSecurity user, Guid[] tenants)>, ApiError> MultiTenantAuthorization(
    HttpContext context,
    Fetcher fetcher,
    Emitter emitter,
    GeneratorSettings settings,
    AuthOptions auth) =>
    from subject in TryGetUser(context, settings.JwtPublicKeys).Apply(Elevate)
    from user in FetchUser(fetcher, subject.Map(ue => new StrongString(ue.SubjectId)))
    from updated in UpdateUser(subject, user, emitter)
    from tenants in AvailableTenants(user, auth).Apply(Ok<Guid[], ApiError>).ToTask().Async()
    select user.Map(u => (u, tenants));

  private static AsyncResult<Option<UserSecurity>, ApiError> FetchUser(
    Fetcher fetcher,
    Option<StrongString> subjectId) =>
    fetcher
      .Fetch<UserSecurity>(subjectId.Map(StrongId (sub) => sub))
      .Map(us => subjectId.Map(sub => us with { Ent = us.Ent.DefaultValue(UserSecurity.Defaulted(sub)) }))
      .Map(fro => fro.Bind(fr => fr.Ent))
      .Map(Ok<Option<UserSecurity>, ApiError>)
      .Async();

  private static AsyncResult<Option<UserEnvelope>, ApiError> Elevate(AsyncOption<UserEnvelope> opt) =>
    opt.Match<Result<Option<UserEnvelope>, ApiError>>(u => Ok(Some(u)), () => Ok(Option<UserEnvelope>.None));

  private static AsyncResult<Unit, ApiError> UpdateUser(
    Option<UserEnvelope> subject,
    Option<UserSecurity> user,
    Emitter emitter) =>
    subject
      .Bind(s => user.Map(u => ChangeEvents(s, u)))
      .Bind(evts => evts.Length != 0 ? Some(evts) : None)
      .Match(EmitUpdates(emitter), () => unit);

  private static Func<EventModelEvent[], AsyncResult<Unit, ApiError>> EmitUpdates(Emitter emitter) =>
    evts => emitter
      .Emit(() => new AnyState(evts).Apply(Ok<EventInsertion, ApiError>).ToTask().Async())
      .Async()
      .Map(_ => unit);

  private static EventModelEvent[] ChangeEvents(UserEnvelope subject, UserSecurity user) =>
    new Option<EventModelEvent>[]
      {
        subject.Name != user.FullName && subject.Name is not null
          ? new UserNameReceived(subject.SubjectId, subject.Name)
          : None,
        subject.Email != user.Email && subject.Email is not null
          ? new UserEmailReceived(subject.SubjectId, subject.Email)
          : None
      }
      .Choose(Id)
      .ToArray();

  private static bool IsProtected(this AuthOptions auth) => auth.Match(_ => false, _ => true, _ => true, _ => true);
}
