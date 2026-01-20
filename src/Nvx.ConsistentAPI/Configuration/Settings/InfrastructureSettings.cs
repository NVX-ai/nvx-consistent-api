namespace Nvx.ConsistentAPI.Configuration.Settings;

/// <summary>
/// Settings for infrastructure connections (databases, storage, SignalR).
/// </summary>
public record InfrastructureSettings(
    string ReadModelConnectionString,
    string EventStoreConnectionString,
    string BlobStorageConnectionString,
    string? AzureSignalRConnectionString = null);
