using Microsoft.AspNetCore.Http;

namespace Nvx.ConsistentAPI.Errors;

public record ConflictError(string Message) : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status409Conflict;
    await context.Response.WriteAsJsonAsync(new ErrorResponse(Message, []));
  }
}
