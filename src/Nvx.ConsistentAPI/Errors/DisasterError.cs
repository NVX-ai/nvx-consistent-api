using Microsoft.AspNetCore.Http;

namespace Nvx.ConsistentAPI.Errors;

public record DisasterError(string Message) : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ErrorResponse(Message, []));
  }
}
