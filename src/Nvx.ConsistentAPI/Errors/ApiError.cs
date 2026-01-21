using Microsoft.AspNetCore.Http;

namespace Nvx.ConsistentAPI.Errors;

public interface ApiError
{
  Task Respond(HttpContext context);
}
