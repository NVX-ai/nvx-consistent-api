# Changelog

## [1.1.1] - 2026-01-20

### Overview

Extracted remaining types from `Generator.cs` into dedicated files within `Configuration/Settings/`, completing the modular architecture refactoring.

### Moved

| Type | Original Location | New Location |
|------|-------------------|--------------|
| `FrameworkFeatures` enum | Generator.cs | Configuration/Settings/FrameworkFeatures.cs |
| `FrameworkFeaturesExtensions` class | Generator.cs | Configuration/Settings/FrameworkFeatures.cs |

### Changed

- **`Generator.cs`** - Now contains only the `Generator` static class (reduced from 228 to 135 lines)
- **`Usings.cs`** - Added global using aliases for backward compatibility:
  - `GeneratorSettings` → `Nvx.ConsistentAPI.Configuration.Settings.GeneratorSettings`
  - `FrameworkFeatures` → `Nvx.ConsistentAPI.Configuration.Settings.FrameworkFeatures`
- **`Usings.cs`** - Added global namespace imports to fix build issues in Configuration modules
- **`TestUtils/Usings.cs`** - Added `Nvx.ConsistentAPI.Configuration.Settings` namespace import

### Breaking Changes

**None.** Types remain accessible from `Nvx.ConsistentAPI` namespace via global using aliases.

### File Changes Summary

| File | Action | Lines |
|------|--------|-------|
| `Generator.cs` | Modified | 228 → 135 |
| `Usings.cs` | Modified | Added global usings |
| `Configuration/Settings/FrameworkFeatures.cs` | Created | 44 |
| `TestUtils/Usings.cs` | Modified | Added namespace import |

---

## [1.1.0] - 2026-01-19

### Overview

Major architectural refactoring of `Generator.cs` and `GeneratorSettings` into a modular, maintainable architecture using composable configuration modules. This release reduces the monolithic 566-line `Generator.cs` file to a slim 236-line orchestrator while maintaining full backward compatibility.

### Added

#### New Settings Records (`Configuration/Settings/`)

- **`InfrastructureSettings`** - Grouped settings for infrastructure connections
  - `ReadModelConnectionString` - SQL Server connection for read models
  - `EventStoreConnectionString` - KurrentDB/EventStoreDB connection
  - `BlobStorageConnectionString` - Azure Blob Storage connection
  - `AzureSignalRConnectionString` - Optional Azure SignalR Service connection

- **`SecuritySettings`** - Grouped settings for security and authentication
  - `JwtPublicKeys` - Public keys for JWT validation
  - `AdminId` - Administrator subject ID
  - `ToolingEndpointsApiKey` - API key for tooling endpoints

- **`PerformanceSettings`** - Grouped settings for performance tuning
  - `ParallelHydration` - Parallelism level for read model hydration (default: 25)
  - `TodoProcessorWorkerCount` - Worker count for todo processor (default: 25)

- **`CustomizationSettings`** - Grouped settings for application customization hooks
  - `BuilderCustomizations` - Optional action to customize WebApplicationBuilder
  - `SwaggerCustomizations` - Optional action to customize SwaggerGenOptions
  - `AppCustomizations` - Optional action to customize WebApplication

#### New Module System (`Configuration/Modules/`)

- **`IGeneratorModule`** - Interface for modular configuration components
  - `Order` property - Determines execution order (lower values execute first)
  - `ConfigureServices()` - Called during service configuration phase
  - `ConfigureApp()` - Called during application configuration phase

- **`ValidationModule`** (Order: -10) - Event cohesion and StrongId validation
  - Validates that each `EventModelEvent` is folded by exactly one entity
  - Validates that obsolete events are not being folded
  - Validates that all `StrongId` subclasses properly override `ToString()`

- **`DapperTypeHandlerModule`** (Order: 0) - Dapper type handler configuration
  - Registers `DateTimeTypeHandler` for UTC DateTime handling
  - Registers `DateOnlyTypeHandler` and `DateOnlyNullableTypeHandler`
  - Registers `ULongTypeHandler` and `ULongNullableTypeHandler`

- **`LoggingModule`** (Order: 5) - Serilog and Application Insights setup
  - Configures Serilog with file and console sinks
  - Integrates Azure Application Insights when configured
  - Filters out `/metrics` endpoint from logs

- **`AuthenticationModule`** (Order: 10) - JWT Bearer authentication
  - Configures JWT Bearer authentication scheme
  - Handles SignalR hub token extraction from query string
  - Sets up authentication and authorization middleware

- **`SwaggerModule`** (Order: 20) - OpenAPI/Swagger documentation
  - Configures Swagger with JWT security scheme
  - Includes `RequiredNotNullableSchemaFilter` for proper nullability
  - Includes `EnumSchemaFilter` for string-based enum serialization
  - Includes `AllOperationsFilter` for operation tagging
  - Handles `X-Forwarded-Prefix` header for reverse proxy support

- **`CorsModule`** (Order: 30) - CORS policy configuration
  - Configures CORS policy with specified origins
  - Allows any header and method from configured origins

- **`SignalRModule`** (Order: 40) - SignalR hub configuration
  - Configures SignalR services
  - Optionally integrates Azure SignalR Service
  - Maps `/message-hub` endpoint

- **`TelemetryModule`** (Order: 50) - OpenTelemetry metrics and tracing
  - Configures OpenTelemetry metrics with Prometheus exporter
  - Configures distributed tracing with OTLP exporter
  - Instruments ASP.NET Core, HTTP client, and custom meters

- **`MiddlewareModule`** (Order: 100) - Custom middleware
  - Increases max request body size for file uploads (350MB)
  - Adds CORS header for Swagger JSON endpoint

#### New Fluent Builder API

- **`GeneratorBuilder`** - Fluent API for advanced application configuration
  ```csharp
  var app = await new GeneratorBuilder()
      .WithPort(5000)
      .WithSettings(settings)
      .WithEventModel(model)
      .WithCorsOrigins("http://localhost:4200")
      .AddModule<CustomHealthCheckModule>()
      .ExcludeModule<SignalRModule>()
      .BuildAsync();
  ```

#### New Generator Methods

- **`Generator.GetDefaultModules(string[] corsOrigins)`** - Returns the default set of modules
- **`Generator.GetWebApp(..., IReadOnlyList<IGeneratorModule> modules)`** - New overload accepting custom modules

#### Computed Properties on GeneratorSettings

- `GeneratorSettings.Infrastructure` - Returns `InfrastructureSettings` view
- `GeneratorSettings.Security` - Returns `SecuritySettings` view
- `GeneratorSettings.Performance` - Returns `PerformanceSettings` view
- `GeneratorSettings.Customization` - Returns `CustomizationSettings` view

### Changed

- **`Generator.cs`** - Reduced from 566 lines to 236 lines (~58% reduction)
  - `GetWebApp()` method reduced from ~265 lines to ~45 lines (~83% reduction)
  - Now orchestrates modules instead of containing all configuration logic
  - Extracted `BuildMergedEventModel()` as private helper method

- **`Logging.cs`** - Reorganized to contain only `PrometheusMetrics` and backward-compatible type exports
  - `LoggingSettings` class remains in `Nvx.ConsistentAPI` namespace for backward compatibility
  - `LogFileRollInterval` enum remains in `Nvx.ConsistentAPI` namespace for backward compatibility

### Moved

The following types were extracted to dedicated module files but remain accessible from their original locations:

| Type | Original Location | New Location |
|------|-------------------|--------------|
| `AllOperationsFilter` | Generator.cs | SwaggerModule.cs |
| `RequiredNotNullableSchemaFilter` | Generator.cs | SwaggerModule.cs |
| `EnumSchemaFilter` | Generator.cs | SwaggerModule.cs |
| `ValidateEventCohesion()` | Generator.cs | ValidationModule.cs |
| `ValidateStrongIds()` | Generator.cs | ValidationModule.cs |

### Deprecated

None.

### Removed

None.

### Fixed

None.

### Security

None.

### Breaking Changes

**None.** This release maintains full backward compatibility:

- The `GeneratorSettings` record constructor signature is unchanged
- The `Generator.GetWebApp(int?, GeneratorSettings, EventModel, string[])` method signature is unchanged
- `LoggingSettings` and `LogFileRollInterval` remain accessible from `Nvx.ConsistentAPI` namespace
- All existing code using the framework will continue to work without modifications

### Migration Guide

No migration required. Existing code will continue to work unchanged.

#### Optional: Using New Features

**Accessing grouped settings:**
```csharp
// Before (still works)
var connectionString = settings.ReadModelConnectionString;

// After (new option)
var connectionString = settings.Infrastructure.ReadModelConnectionString;
```

**Customizing modules:**
```csharp
// Exclude SignalR module
var modules = Generator.GetDefaultModules(corsOrigins)
    .Where(m => m is not SignalRModule)
    .ToList();

var app = await Generator.GetWebApp(port, settings, eventModel, corsOrigins, modules);

// Or use the fluent builder
var app = await new GeneratorBuilder()
    .WithSettings(settings)
    .WithEventModel(eventModel)
    .ExcludeModule<SignalRModule>()
    .BuildAsync();
```

**Adding custom modules:**
```csharp
public class HealthCheckModule : IGeneratorModule
{
    public int Order => 60; // After TelemetryModule

    public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel)
    {
        builder.Services.AddHealthChecks();
    }

    public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel)
    {
        app.MapHealthChecks("/health");
    }
}

var app = await new GeneratorBuilder()
    .WithSettings(settings)
    .WithEventModel(eventModel)
    .AddModule<HealthCheckModule>()
    .BuildAsync();
```

### File Changes Summary

| File | Action | Lines |
|------|--------|-------|
| `Generator.cs` | Modified | 566 → 236 |
| `Logging.cs` | Modified | 111 → 117 |
| `GeneratorBuilder.cs` | Created | 103 |
| `Configuration/Settings/InfrastructureSettings.cs` | Created | 11 |
| `Configuration/Settings/SecuritySettings.cs` | Created | 12 |
| `Configuration/Settings/PerformanceSettings.cs` | Created | 10 |
| `Configuration/Settings/CustomizationSettings.cs` | Created | 13 |
| `Configuration/Modules/IGeneratorModule.cs` | Created | 27 |
| `Configuration/Modules/ValidationModule.cs` | Created | 109 |
| `Configuration/Modules/DapperTypeHandlerModule.cs` | Created | 28 |
| `Configuration/Modules/LoggingModule.cs` | Created | 55 |
| `Configuration/Modules/AuthenticationModule.cs` | Created | 62 |
| `Configuration/Modules/SwaggerModule.cs` | Created | 141 |
| `Configuration/Modules/CorsModule.cs` | Created | 35 |
| `Configuration/Modules/SignalRModule.cs` | Created | 27 |
| `Configuration/Modules/TelemetryModule.cs` | Created | 57 |
| `Configuration/Modules/MiddlewareModule.cs` | Created | 35 |

### Contributors

- Refactoring implemented with assistance from Claude Code
