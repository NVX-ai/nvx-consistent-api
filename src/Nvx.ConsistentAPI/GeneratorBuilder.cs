using Nvx.ConsistentAPI.Configuration.Modules;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Fluent builder for creating a ConsistentApp with customizable modules.
/// </summary>
public class GeneratorBuilder
{
    private int? _port;
    private GeneratorSettings? _settings;
    private EventModel? _eventModel;
    private string[] _corsOrigins = [];
    private readonly List<IGeneratorModule> _additionalModules = [];
    private readonly HashSet<Type> _excludedModuleTypes = [];

    /// <summary>
    /// Sets the port for the web application.
    /// </summary>
    public GeneratorBuilder WithPort(int port)
    {
        _port = port;
        return this;
    }

    /// <summary>
    /// Sets the generator settings.
    /// </summary>
    public GeneratorBuilder WithSettings(GeneratorSettings settings)
    {
        _settings = settings;
        return this;
    }

    /// <summary>
    /// Sets the event model.
    /// </summary>
    public GeneratorBuilder WithEventModel(EventModel eventModel)
    {
        _eventModel = eventModel;
        return this;
    }

    /// <summary>
    /// Sets the allowed CORS origins.
    /// </summary>
    public GeneratorBuilder WithCorsOrigins(params string[] corsOrigins)
    {
        _corsOrigins = corsOrigins;
        return this;
    }

    /// <summary>
    /// Adds a custom module to the generator.
    /// </summary>
    public GeneratorBuilder AddModule(IGeneratorModule module)
    {
        _additionalModules.Add(module);
        return this;
    }

    /// <summary>
    /// Adds a custom module of the specified type to the generator.
    /// </summary>
    public GeneratorBuilder AddModule<TModule>() where TModule : IGeneratorModule, new()
    {
        _additionalModules.Add(new TModule());
        return this;
    }

    /// <summary>
    /// Excludes a default module of the specified type from the generator.
    /// </summary>
    public GeneratorBuilder ExcludeModule<TModule>() where TModule : IGeneratorModule
    {
        _excludedModuleTypes.Add(typeof(TModule));
        return this;
    }

    /// <summary>
    /// Builds the ConsistentApp with the configured modules.
    /// </summary>
    public async Task<ConsistentApp> BuildAsync()
    {
        if (_settings is null)
        {
          throw new InvalidOperationException("Settings must be provided. Call WithSettings() first.");
        }

        if (_eventModel is null)
        {
          throw new InvalidOperationException("EventModel must be provided. Call WithEventModel() first.");
        }

        var modules = GetModules();
        return await Generator.GetWebApp(_port, _settings, _eventModel, _corsOrigins, modules);
    }

    private IReadOnlyList<IGeneratorModule> GetModules()
    {
        var defaultModules = Generator.GetDefaultModules(_corsOrigins)
            .Where(m => !_excludedModuleTypes.Contains(m.GetType()))
            .ToList();

        defaultModules.AddRange(_additionalModules);
        return defaultModules;
    }
}
