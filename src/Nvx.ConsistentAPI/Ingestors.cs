using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.Store.Events;

namespace Nvx.ConsistentAPI;

public interface Ingestor
{
  string AreaTag { get; }
  Task<Option<EventModelEvent>> Ingest(HttpContext context, Fetcher fetcher);
  public void SetOpenApi(OpenApiOperation operation) { }
}

public static class IngestorExtensions
{
  public static void ApplyTo(
    this Ingestor ingestor,
    WebApplication app,
    Fetcher fetcher,
    Emitter emitter,
    GeneratorSettings settings)
  {
    if (!settings.EnabledFeatures.HasIngestors())
    {
      return;
    }

    app
      .MapPost(
        $"/ingestor/{ingestor.GetType().Apply(Naming.ToSpinalCase)}",
        async context => await ingestor
          .Ingest(context, fetcher)
          .Async()
          .Iter(async e => await emitter.Emit(() => new AnyState(e))))
      .WithOpenApi(o =>
      {
        o.Tags = [new OpenApiTag { Name = ingestor.AreaTag }];
        ingestor.SetOpenApi(o);
        return o;
      });
  }
}
