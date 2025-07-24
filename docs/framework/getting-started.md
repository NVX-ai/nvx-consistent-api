# Getting started
The Consistent API framework only needs two elements to generate an event sourcing API:

- An Event Model.
- A few configuration options.

The signature of the framework entry point is:
```cs
public static class Generator
{
  public static async Task<WebApplication> GetWebApp(
    int? port,
    GeneratorSettings settings,
    EventModel eventModel,
    string[] corsOrigins) { /*...*/ }
```

```cs
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
  int ParallelHydration = 25);
```

```cs
public class LoggingSettings
{
  public string? LogsFolder { get; init; }
  public string? AzureInstrumentationKey { get; init; }
  public LogLevel LogLevel { get; init; } = LogLevel.Trace;
  public bool UseConsoleLogger { get; init; } = true;
  public string? TracingOpenTelemetryEndpoint { get; init; }
  public LogFileRollInterval LogFileRollInterval { get; init; } = LogFileRollInterval.Hour;
  public int LogDaysToKeep { get; init; } = 7;
  public bool AddMetricsAndTracingToConsole { get; init; } = false;
}
```

```cs
public enum FrameworkFeatures
{
  None = 0,
  ReadModelHydration = 1 << 0,
  ReadModelEndpoints = 1 << 1,
  StaticEndpoints = 1 << 2,
  SystemEndpoints = 1 << 3,
  Commands = 1 << 4,
  Projections = 1 << 5,
  Tasks = 1 << 6,
  Ingestors = 1 << 7,

  All = ReadModelHydration
        | ReadModelEndpoints
        | StaticEndpoints
        | SystemEndpoints
        | Commands
        | Projections
        | Tasks
        | Ingestors
}
```


## Port
An optional startup port, it is currently used for testing purposes.

## Settings
Configuration settings for the framework.
### ReadModelConnectionString
The connection string for the read model database, a Microsoft SQL database.
### EventStoreConnectionString
The connection string for EventStore DB.
### BlobStorageConnectionString
The connection string for the blob storage.
### JwtPublicKeys
The public keys for JWT validation.
### AdminId
The subject ID of the admin user, this will assign the admin role to that user if said user does not have the role already.
### AzureSignalRConnectionString
The connection string for Azure SignalR, the application will default to local SignalR if this is not set.
### BuilderCustomizations
Customizations to the web application builder.
### SwaggerCustomizations
Customizations to the swagger generator.
### AppCustomizations
Customizations to the web application.
### LoggingSettings
#### LogFolder
Setting this value will direct serilog to store activity logs in the indicated folder.
#### AzureInstrumentationKey
Setting it will enable the framework to trace and log to azure app insights. **By default the framework is properly chatty, watch out for storage costs**.
#### TracingOpenTelemetryEndpoint
Url of an open telemetry service, we use prometheus at the moment.
### ToolingEndpointsApiKey
The framework provides several endpoints with telemetry specific to event sourcing such as read model hydration state or running integrations, said endpoints can be accessed by application admins and via this key.
### EnabledFeatures
These flags are self explanatory after the features of the framework are understood, most features can be toggled independently, but Tasks necessitate read models and projections.
