using Microsoft.AspNetCore.Http;

namespace Nvx.ConsistentAPI.Errors;

public record CorruptStreamError(string EntityName, string Id) : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ErrorResponse($"Stream {EntityName} with id {Id} is corrupt", []));
  }
}
