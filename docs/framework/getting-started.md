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
  Option<Action<WebApplication>> AppCustomizations);
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
