using Microsoft.AspNetCore.Builder;

namespace Nvx.ConsistentAPI.EventModeling;

public interface EventModelingCommandArtifact : Endpoint
{
  /// <summary>
  ///   Called by the framework to wire up the command.
  /// </summary>
  /// <param name="app">The web app that will expose the API.</param>
  /// <param name="fetcher">Entity fetcher.</param>
  /// <param name="emitter">Event emitter.</param>
  /// <param name="settings">Framework settings.</param>
  /// <param name="logger">Logger instance.</param>
  void ApplyTo(WebApplication app, Fetcher fetcher, Emitter emitter, GeneratorSettings settings, ILogger logger);
}
