using Microsoft.AspNetCore.Builder;

namespace Nvx.ConsistentAPI.Configuration.Modules;

/// <summary>
/// Interface for modular configuration components of the Generator.
/// Modules are executed in order of their Order property.
/// </summary>
public interface IGeneratorModule
{
    /// <summary>
    /// The order in which this module should be executed.
    /// Lower values execute first. Negative values are allowed for early-stage modules.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Configure services on the WebApplicationBuilder.
    /// Called during the service configuration phase.
    /// </summary>
    public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel);

    /// <summary>
    /// Configure the WebApplication after it has been built.
    /// Called during the application configuration phase.
    /// </summary>
    public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel);
}
