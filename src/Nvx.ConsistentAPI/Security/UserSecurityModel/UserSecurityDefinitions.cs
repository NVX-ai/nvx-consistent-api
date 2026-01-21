using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nvx.ConsistentAPI.Configuration.Settings;

namespace Nvx.ConsistentAPI;

public static class UserSecurityDefinitions
{
  public static void InitializeEndpoints(
    WebApplication app,
    Emitter emitter,
    Fetcher fetcher,
    GeneratorSettings settings)
  {
    if (!settings.EnabledFeatures.HasEndpoints())
    {
      return;
    }

    Delegate handler = async (HttpContext context) =>
      await GetPermissions(context, None).Apply(Respond<string[]>(context));

    app
      .MapGet("/current-user/permissions", handler)
      .Produces<string[]>()
      .Produces<ErrorResponse>(500)
      .WithTags(OperationTags.CurrentUser)
      .ApplyAuth(new EveryoneAuthenticated());

    Delegate tenantHandler = async (HttpContext context, Guid tenantId) =>
      await GetPermissions(context, tenantId).Apply(Respond<string[]>(context));

    app
      .MapGet("/tenant/{tenantId:Guid}/current-user/permissions", tenantHandler)
      .Produces<string[]>()
      .Produces<ErrorResponse>(500)
      .WithTags(OperationTags.CurrentUser)
      .ApplyAuth(new EveryoneAuthenticated());

    Delegate definitionHandler = async (HttpContext context) =>
      await GetUser(context).Apply(Respond<UserSecurity>(context));

    app
      .MapGet("/current-user", definitionHandler)
      .Produces<UserSecurity>()
      .Produces<ErrorResponse>(500)
      .WithTags(OperationTags.CurrentUser)
      .ApplyAuth(new EveryoneAuthenticated());

    return;

    AsyncResult<string[], ApiError> GetPermissions(HttpContext context, Option<Guid> tenantId) =>
      FrameworkSecurity
        .Authorization(context, fetcher, emitter, settings, new EveryoneAuthenticated(), tenantId)
        .Bind(opt =>
          opt.Result<ApiError>(new UnauthorizedError()).Map(FrameworkSecurity.EffectivePermissions(tenantId)));

    AsyncResult<UserSecurity, ApiError> GetUser(HttpContext context) =>
      FrameworkSecurity
        .Authorization(context, fetcher, emitter, settings, new EveryoneAuthenticated(), None)
        .Bind(opt => opt.Result<ApiError>(new UnauthorizedError()));

    static Func<AsyncResult<T, ApiError>, Task> Respond<T>(HttpContext context) =>
      async r =>
      {
        await r.Iter(
          async value =>
          {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(value);
          },
          async error => await error.Respond(context));
      };
  }
}
