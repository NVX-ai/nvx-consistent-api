using Microsoft.AspNetCore.Http;

namespace Nvx.ConsistentAPI;

public interface ApiError
{
  Task Respond(HttpContext context);
}

public record ErrorResponse(string Message, string[] Errors);

public record ValidationError(string[] Errors) : ApiError
{
  public async Task Respond(HttpContext context)
  {
    var response = new ErrorResponse("Invalid request", Errors);
    context.Response.StatusCode = StatusCodes.Status400BadRequest;
    await context.Response.WriteAsJsonAsync(response);
  }
}

public record NotFoundError(string EntityName, string Id) : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    await context.Response.WriteAsJsonAsync(new ErrorResponse($"Could not find entity {EntityName} with id {Id}", []));
  }
}

public record DisasterError(string Message) : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ErrorResponse(Message, []));
  }
}

public record ConflictError(string Message) : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status409Conflict;
    await context.Response.WriteAsJsonAsync(new ErrorResponse(Message, []));
  }
}

public record UnauthorizedError : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsJsonAsync(new ErrorResponse("Unauthorized request", []));
  }
}

public record ForbiddenError : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status403Forbidden;
    await context.Response.WriteAsJsonAsync(new ErrorResponse("Access is forbidden", []));
  }
}

public record CorruptStreamError(string EntityName, string Id) : ApiError
{
  public async Task Respond(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ErrorResponse($"Stream {EntityName} with id {Id} is corrupt", []));
  }
}
