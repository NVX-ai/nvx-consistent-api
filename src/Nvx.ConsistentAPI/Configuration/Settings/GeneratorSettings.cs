using Microsoft.AspNetCore.Builder;
using Microsoft.IdentityModel.Tokens;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nvx.ConsistentAPI.Configuration.Settings;

public record GeneratorSettings(
    string ReadModelConnectionString,
    string EventStoreConnectionString,
    string BlobStorageConnectionString,
    SecurityKey[] JwtPublicKeys,
    string AdminId,
    string? AzureSignalRConnectionString,
    Option<Action<WebApplicationBuilder>> BuilderCustomizations,
    Option<Action<SwaggerGenOptions>> SwaggerCustomizations,
    Option<Action<WebApplication>> AppCustomizations,
    LoggingSettings LoggingSettings,
    string ToolingEndpointsApiKey,
    FrameworkFeatures EnabledFeatures = FrameworkFeatures.All,
    int ParallelHydration = 25,
    int TodoProcessorWorkerCount = 25)
{
    /// <summary>
    /// Infrastructure settings (connection strings).
    /// </summary>
    public InfrastructureSettings Infrastructure => new(
        ReadModelConnectionString,
        EventStoreConnectionString,
        BlobStorageConnectionString,
        AzureSignalRConnectionString);

    /// <summary>
    /// Security settings (JWT, admin, API keys).
    /// </summary>
    public SecuritySettings Security => new(
        JwtPublicKeys,
        AdminId,
        ToolingEndpointsApiKey);

    /// <summary>
    /// Performance settings (parallelism, worker counts).
    /// </summary>
    public PerformanceSettings Performance => new(
        ParallelHydration,
        TodoProcessorWorkerCount);

    /// <summary>
    /// Customization settings (action hooks).
    /// </summary>
    public CustomizationSettings Customization => new(
        BuilderCustomizations,
        SwaggerCustomizations,
        AppCustomizations);
}
