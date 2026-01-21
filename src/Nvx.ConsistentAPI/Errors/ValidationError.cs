using Microsoft.AspNetCore.Http;

namespace Nvx.ConsistentAPI.Errors;

public record ValidationError(string[] Errors) : ApiError
{
  public async Task Respond(HttpContext context)
  {
    var response = new ErrorResponse("Invalid request", Errors);
    context.Response.StatusCode = StatusCodes.Status400BadRequest;
    await context.Response.WriteAsJsonAsync(response);
  }
}
