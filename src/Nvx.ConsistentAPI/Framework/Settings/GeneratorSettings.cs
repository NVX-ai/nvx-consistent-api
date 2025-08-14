using Microsoft.AspNetCore.Builder;
using Microsoft.IdentityModel.Tokens;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nvx.ConsistentAPI;

public record GeneratorSettings(
  string ReadModelConnectionString,
  EventStoreSettings EventStoreSettings,
  string BlobStorageConnectionString,
  SecurityKey[] JwtPublicKeys,
  string AdminId,
  string? AzureSignalRConnectionString,
  Option<Action<WebApplicationBuilder>> BuilderCustomizations,
  Option<Action<SwaggerGenOptions>> SwaggerCustomizations,
  Option<Action<WebApplication>> AppCustomizations,
  LoggingSettings LoggingSettings,
  string ToolingEndpointsApiKey,
  FrameworkFeatures EnabledFeatures,
  int ParallelHydration);
