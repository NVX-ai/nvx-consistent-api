using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.Store.Events;

namespace Nvx.ConsistentAPI.Framework.StaticEndpoints;

public interface StaticEndpointArtifact
{
  void ApplyTo(
    WebApplication app,
    Fetcher fetcher,
    Emitter emitter,
    GeneratorSettings settings);
}

public delegate Result<Shape, ApiError> GetStaticResponse<Shape>(Option<UserSecurity> user);

public class StaticEndpointDefinition<Shape> : StaticEndpointArtifact
{
  public required GetStaticResponse<Shape> GetStaticResponse { get; init; }
  public required string AreaTag { private get; init; }
  public Action<OpenApiOperation> OpenApiCustomizer { get; init; } = _ => { };
  public string? Description { get; init; } = null;
  public AuthOptions Auth { get; init; } = new Everyone();

  public void ApplyTo(
    WebApplication app,
    Fetcher fetcher,
    Emitter emitter,
    GeneratorSettings settings)
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.StaticEndpoints))
    {
      return;
    }

    Delegate handle = async (HttpContext context) =>
    {
      await Authorize()
        .Bind(GetResponse)
        .Iter(
          async value =>
          {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response
              .WriteAsJsonAsync(
                value,
                new JsonSerializerOptions
                {
                  Converters = { new UtcDateTimeConverter() },
                  PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
          },
          async error => await error.Respond(context));

      return;

      AsyncResult<Option<UserSecurity>, ApiError> Authorize() =>
        FrameworkSecurity.Authorization(context, fetcher, emitter, settings, Auth, None);

      AsyncResult<Shape, ApiError> GetResponse(Option<UserSecurity> user) =>
        GetStaticResponse(user);
    };

    app
      .MapGet($"/static/{Naming.ToSpinalCase<Shape>()}", handle)
      .WithName(typeof(Shape).Name)
      .Produces<Shape>()
      .Produces<ErrorResponse>(404)
      .Produces<ErrorResponse>(500)
      .WithOpenApi(o =>
      {
        o.OperationId = $"get{typeof(Shape).Name}";
        o.Tags = [new OpenApiTag { Name = AreaTag }];
        if (!string.IsNullOrWhiteSpace(Description))
        {
          o.Description = Description;
        }

        OpenApiCustomizer(o);
        return o;
      })
      .ApplyAuth(Auth);
  }
}
