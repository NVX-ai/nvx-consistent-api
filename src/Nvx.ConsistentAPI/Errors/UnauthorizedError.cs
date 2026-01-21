using Microsoft.AspNetCore.Http;

namespace Nvx.ConsistentAPI.Errors;

public record UnauthorizedError : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsJsonAsync(new ErrorResponse("Unauthorized request", []));
  }
}
