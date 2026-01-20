using Microsoft.AspNetCore.Builder;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nvx.ConsistentAPI.Configuration.Settings;

/// <summary>
/// Settings for application customization hooks.
/// </summary>
public record CustomizationSettings(
    Option<Action<WebApplicationBuilder>> BuilderCustomizations,
    Option<Action<SwaggerGenOptions>> SwaggerCustomizations,
    Option<Action<WebApplication>> AppCustomizations);
