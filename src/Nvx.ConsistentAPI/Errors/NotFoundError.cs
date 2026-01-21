using Microsoft.AspNetCore.Http;

namespace Nvx.ConsistentAPI.Errors;

public record NotFoundError(string EntityName, string Id) : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    await context.Response.WriteAsJsonAsync(new ErrorResponse($"Could not find entity {EntityName} with id {Id}", []));
  }
}
