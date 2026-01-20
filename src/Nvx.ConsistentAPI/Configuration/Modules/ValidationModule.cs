using System.Reflection;
using Microsoft.AspNetCore.Builder;

namespace Nvx.ConsistentAPI.Configuration.Modules;

/// <summary>
/// Module that validates event cohesion and StrongId implementations.
/// Runs early to catch configuration errors before other setup.
/// </summary>
public class ValidationModule : IGeneratorModule
{
    public int Order => -10;

    public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel)
    {
        ValidateEventCohesion();
        ValidateStrongIds();
    }

    public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel)
    {
        // No app configuration needed
    }

    internal static void ValidateEventCohesion()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        var foldTypes = new List<Type>();
        var eventModelEventTypes = new List<Type>();

        foreach (var assembly in assemblies)
        {
            // There is a bug with the test runner that prevents loading some types
            // from system data while running tests.
            if (assembly.FullName?.StartsWith("System.Data.") ?? false)
            {
                continue;
            }

            foldTypes.AddRange(
                assembly
                    .GetTypes()
                    .Where(type =>
                        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Folds<,>))
                    )
            );

            eventModelEventTypes.AddRange(
                assembly
                    .GetTypes()
                    .Where(type =>
                        typeof(EventModelEvent).IsAssignableFrom(type) && !type.IsAbstract
                    )
            );
        }

        foreach (var eventModelEventType in eventModelEventTypes)
        {
            var foldCountForEvent = foldTypes.Count(foldType => foldType
                .GetInterfaces()
                .Any(i =>
                    i.IsGenericType
                    && i.GetGenericTypeDefinition() == typeof(Folds<,>)
                    && i.GetGenericArguments()[0] == eventModelEventType
                )
            );

            var isObsolete = eventModelEventType.GetCustomAttribute<ObsoleteAttribute>() is not null;
            if (isObsolete && foldCountForEvent != 0)
            {
                throw new Exception(
                    $"The event type '{eventModelEventType.Name}' is marked as obsolete and should not be folded.");
            }

            if (foldCountForEvent != 1 && !isObsolete)
            {
                throw new Exception(
                    $"The event type '{eventModelEventType.Name}' is being fold in {foldCountForEvent} entities, expected exactly one.");
            }
        }
    }

    private static void ValidateStrongIds()
    {
        // Strong IDs should override ToString to return StreamId,
        // since there is no sensible way to model this in the type
        // system, this test will scan all StrongId subclasses and
        // verify that the ToString method returns the StreamId.
        // The reason behind this is that it's really easy to write
        // a method to get a stream name by interpolating the StrongId,
        // which will automatically call ToString, and ToString is
        // automatically generated for pretty printing in records,
        // ensuring chaos and brimstone.
        var types = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(a => !a.FullName?.StartsWith("System.Data.") ?? false)
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsSubclassOf(typeof(StrongId)));

        foreach (var type in types)
        {
            var constructor = type.GetConstructors().First();
            var parameters = constructor.GetParameters();
            var values =
                from parameter in parameters
                select parameter switch
                {
                    _ when parameter.ParameterType == typeof(string) => Guid.NewGuid().ToString(),
                    { ParameterType.IsValueType: true } => Activator.CreateInstance(parameter.ParameterType),
                    _ => throw new NotSupportedException("Use only primitives for strong id properties.")
                };
            var instance = (StrongId)constructor.Invoke(values.ToArray());
            if (instance.ToString() != instance.StreamId())
            {
                throw new Exception($"StrongId {type.Name} does not override ToString to return StreamId");
            }
        }
    }
}
