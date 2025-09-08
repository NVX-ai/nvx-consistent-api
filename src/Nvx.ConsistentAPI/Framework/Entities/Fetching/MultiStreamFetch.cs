using EventStore.Client;
using Microsoft.Extensions.Caching.Memory;

namespace Nvx.ConsistentAPI;

internal static class MultiStreamFetch
{
  internal static async Task<FetchResult<Entity>> Do<Entity>(
    MemoryCache cache,
    bool resetCache,
    Entity defaulted,
    Position? upToRevision,
    EventStoreClient client,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    MemoryCacheEntryOptions entryOptions,
    Fetcher fetcher,
    string[] interests) where Entity : EventModelEntity<Entity>
  {
    var cached = resetCache ? new Miss() : cache.Find(defaulted.GetStreamName());
    var allStreams = interests
      .Append(defaulted.GetStreamName())
      .Distinct()
      .ToArray();

    var revisions = cached is MultipleStreamCacheResult<Entity> m
      ? m.StreamRevisions
      : new Dictionary<string, long>();

    (Entity e, Option<Position> gp, Option<long> r, DateTime? fe, DateTime? le, string? fu, string? lu) seed =
      cached switch
      {
        MultipleStreamCacheResult<Entity> multiple =>
        (
          multiple.Entity,
          multiple.GlobalPosition,
          multiple.Revision,
          multiple.FirstEventAt,
          multiple.LastEventAt,
          multiple.FirstUserSubFound,
          multiple.LastUserSubFound),
        _ => (defaulted, None, None, null, null, null, null)
      };

    var hadEvents = seed.gp.IsSome;
    await foreach (var re in Zip(allStreams, client, revisions))
    {
      foreach (var parsed in parser(re))
      {
        if (parsed.GetStreamName() == seed.e.GetStreamName() && re.Event.Position > upToRevision)
        {
          continue;
        }

        hadEvents = true;
        revisions[re.Event.EventStreamId] = re.Event.EventNumber.ToInt64();

        var metadata = EventMetadata.TryParse(re);
        var firstEventAt = seed.fe ?? metadata.CreatedAt;
        var lastEventAt = metadata.CreatedAt;
        var firstUserSubFound = seed.fu ?? metadata.RelatedUserSub;
        var lastUserSubFound = metadata.RelatedUserSub ?? seed.lu;

        var folded = await seed.e.Fold(
          parsed,
          metadata,
          new RevisionFetchWrapper(fetcher, re.OriginalEvent.Position, resetCache));

        var revision = re.Event.EventStreamId == seed.e.GetStreamName()
          ? re.Event.EventNumber.ToInt64()
          : seed.r;

        seed = (
          folded,
          re.Event.Position,
          revision,
          firstEventAt,
          lastEventAt,
          firstUserSubFound,
          lastUserSubFound
        );
      }
    }

    if (hadEvents && upToRevision is null)
    {
      cache.Set(
        seed.e.GetStreamName(),
        new MultipleStreamCacheResult<Entity>(
          seed.e,
          seed.gp,
          seed.r,
          revisions,
          seed.fe ?? DateTime.UtcNow,
          seed.le ?? DateTime.UtcNow,
          seed.fu,
          seed.lu),
        entryOptions);
    }

    return hadEvents || seed.gp.IsSome
      ? new FetchResult<Entity>(seed.e, seed.r.DefaultValue(0), seed.gp, seed.fe, seed.le, seed.fu, seed.lu)
      : new FetchResult<Entity>(None, -1, None, null, null, null, null);
  }
   private static async IAsyncEnumerable<ResolvedEvent> Zip(
    string[] streamNames,
    EventStoreClient client,
    Dictionary<string, long> streamRevisions)
  {
    var zipWrappers = streamNames
      .Select(s => new StreamZipWrapper(
        client.ReadStreamAsync(
          Direction.Forwards,
          s,
          streamRevisions.TryGetValue(s, out var r)
            ? StreamPosition.FromInt64(r + 1)
            : StreamPosition.Start)))
      .ToArray();

    while (zipWrappers.Any(w => !w.IsDone))
    {
      await Task.WhenAll(zipWrappers.Select(z => z.TryLoadNext()));
      var nextWrapper = zipWrappers.Where(w => !w.IsDone).OrderBy(w => w.Position).FirstOrDefault();
      if (nextWrapper?.TryPop() is { } nextEvent)
      {
        yield return nextEvent;
      }
    }
  }

  private class StreamZipWrapper(EventStoreClient.ReadStreamResult stream)
  {
    private readonly IAsyncEnumerator<ResolvedEvent> enumerator = stream.GetAsyncEnumerator();
    private readonly Lazy<Task<ReadState>> readState = new(() => stream.ReadState);
    private ResolvedEvent? current;
    public bool IsDone { get; private set; }

    public Position? Position => current?.Event.Position;

    public ResolvedEvent? TryPop()
    {
      var result = current;
      current = null;
      return result;
    }

    public async Task TryLoadNext()
    {
      if (current is not null)
      {
        return;
      }

      if (await readState.Value == ReadState.StreamNotFound)
      {
        IsDone = true;
        return;
      }

      if (await enumerator.MoveNextAsync())
      {
        current = enumerator.Current;
      }
      else
      {
        IsDone = true;
      }
    }
  }
}
