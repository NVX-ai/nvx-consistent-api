using KurrentDB.Client;
using Microsoft.AspNetCore.Builder;
using Nvx.ConsistentAPI.InternalTooling;

namespace Nvx.ConsistentAPI.EventModeling;

public interface EventModelingReadModelArtifact : Endpoint
{
  Type ShapeType { get; }
  Task<SingleReadModelInsights> Insights(ulong lastEventPosition, KurrentDBClient eventStoreClien);

  Task ApplyTo(
    WebApplication app,
    KurrentDBClient esClient,
    Fetcher fetcher,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    Emitter emitter,
    GeneratorSettings settings,
    ILogger logger,
    string modelHash);

  bool IsUpToDate(ulong? position = null);
}
