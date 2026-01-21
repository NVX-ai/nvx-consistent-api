using Microsoft.AspNetCore.Http;

namespace Nvx.ConsistentAPI.Errors;

public record ForbiddenError : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status403Forbidden;
    await context.Response.WriteAsJsonAsync(new ErrorResponse("Access is forbidden", []));
  }
}
