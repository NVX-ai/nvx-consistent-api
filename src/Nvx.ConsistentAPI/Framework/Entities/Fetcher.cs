using EventStore.Client;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace Nvx.ConsistentAPI;

public interface EntityFetcher
{
  Task<FetchResult<T>> Fetch<T>(Option<StrongId> id, Position? upToRevision, Fetcher fetcher)
    where T : EventModelEntity<T>;

  internal AsyncOption<T> WrappedFetch<T>(Option<StrongId> id, Fetcher fetcher, Position? upToRevision, bool resetCache)
    where T : EventModelEntity<T>;

  internal AsyncOption<FoundEntity> DaemonFetch(Option<StrongId> id, Fetcher fetcher, bool resetCache = false);

  Type GetFetchType();

  internal bool CanProcessStream(string streamName);
  internal Option<long> GetCachedStreamRevision(StrongId id);
}

public interface RevisionFetcher
{
  AsyncOption<T> Fetch<T>(Option<StrongId> id) where T : EventModelEntity<T>;
  AsyncOption<T> LatestFetch<T>(Option<StrongId> id) where T : EventModelEntity<T>;
}

internal class RevisionFetchWrapper(Fetcher fetcher, Position upToRevision) : RevisionFetcher
{
  public AsyncOption<T> Fetch<T>(Option<StrongId> id) where T : EventModelEntity<T> =>
    fetcher.WrappedFetch<T>(id, upToRevision, resetCache);

  public AsyncOption<T> LatestFetch<T>(Option<StrongId> id) where T : EventModelEntity<T> =>
    fetcher.WrappedFetch<T>(id, null, resetCache);
}

public class Fetcher
{
  private readonly EntityFetcher[] fetchers;

  internal Fetcher(IEnumerable<EntityFetcher> fetchers)
  {
    this.fetchers = fetchers.ToArray();
  }

  internal Option<long> GetCachedStreamRevision(string streamName, StrongId id) =>
    fetchers
      .SingleOrNone(f => f.CanProcessStream(streamName))
      .Bind(f => f.GetCachedStreamRevision(id));

  internal AsyncOption<T> WrappedFetch<T>(Option<StrongId> id, Position? upToRevision, bool resetCache)
    where T : EventModelEntity<T> =>
    fetchers
      .SingleOrNone(f => f.GetFetchType() == typeof(T))
      .Match(f => f.WrappedFetch<T>(id, this, upToRevision, resetCache), () => throw new InvalidOperationException());


  internal AsyncOption<FoundEntity> DaemonFetch(Option<StrongId> id, string streamName, bool resetCache = false) =>
    fetchers
      .SingleOrNone(f => f.CanProcessStream(streamName))
      .Match(
        f => f.DaemonFetch(id, this, resetCache),
        () => Option<FoundEntity>.None.ToTask());

  public Task<FetchResult<T>> Fetch<T>(Option<StrongId> id, Position? upToRevision = null)
    where T : EventModelEntity<T> =>
    fetchers
      .SingleOrNone(f => f.GetFetchType() == typeof(T))
      .Match(f => f.Fetch<T>(id, upToRevision, this), () => throw new InvalidOperationException());

  public AsyncResult<FetchResult<T>, ApiError> SafeFetch<T>(
    Option<StrongId> id,
    Position? upToRevision = null) where T : EventModelEntity<T>
  {
    try
    {
      return fetchers
        .SingleOrNone(f => f.GetFetchType() == typeof(T))
        .Match<Task<Result<FetchResult<T>, ApiError>>>(
          f => f.Fetch<T>(id, upToRevision, this).Map(Ok<FetchResult<T>, ApiError>),
          () => new DisasterError("No fetcher found for type").Apply(Error<FetchResult<T>, ApiError>).ToTask());
    }
    catch (JsonSerializationException)
    {
      return new CorruptStreamError(
          typeof(T).Name,
          id.DefaultValue(new StrongString("unknown")).StreamId())
        .Apply(Error<FetchResult<T>, ApiError>);
    }
    catch (Exception e)
    {
      return new DisasterError(e.Message).Apply(Error<FetchResult<T>, ApiError>);
    }
  }
}

public class Fetcher<Entity> : EntityFetcher
  where Entity : EventModelEntity<Entity>
{
  private readonly MemoryCache cache;
  private readonly Type entityType = typeof(Entity);

  private readonly Func<Option<StrongId>, Position?, Fetcher, bool, Task<FetchResult<Entity>>> fetch;

  private readonly string streamPrefix;

  public Fetcher(
    EventStoreClient client,
    Func<StrongId, Option<Entity>> defaulter,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    int cacheSize,
    TimeSpan cacheExpiration,
    bool isSlidingCache,
    string streamPrefix)
  {
    this.streamPrefix = streamPrefix;
    cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = cacheSize });
    fetch = Build(
      client,
      defaulter,
      parser,
      cache,
      cacheExpiration,
      isSlidingCache);
  }

  public AsyncOption<FoundEntity> DaemonFetch(Option<StrongId> id, Fetcher fetcher, bool resetCache = false) =>
    Fetch(id, null, fetcher, resetCache).Map(FoundEntity<Entity>.From).Async().Map(FoundEntity (fe) => fe);

  public Type GetFetchType() => entityType;
  public bool CanProcessStream(string streamName) => streamName.StartsWith(streamPrefix);

  public Option<long> GetCachedStreamRevision(StrongId id) =>
    cache.TryGetValue<CacheResult>(id.StreamId(), out var cachedEntity)
      ? cachedEntity switch
      {
        SingleStreamCacheResult<Entity> single => single.Revision,
        MultipleStreamCacheResult<Entity> multiple =>
          multiple.StreamRevisions.TryGetValue(id.StreamId(), out var revision) ? Some(revision) : None,
        _ => None
      }
      : None;

  public Task<FetchResult<T>> Fetch<T>(Option<StrongId> id, Position? upToRevision, Fetcher fetcher)
    where T : EventModelEntity<T> =>
    Fetch(id, upToRevision, fetcher)
      .Map(fr => new FetchResult<T>(
        fr.Ent.Map(e => (T)(object)e),
        fr.Revision,
        fr.GlobalPosition,
        fr.FirstEventAt,
        fr.LastEventAt,
        fr.FirstUserSubFound,
        fr.LastUserSubFound));

  AsyncOption<T> EntityFetcher.WrappedFetch<T>(
    Option<StrongId> id,
    Fetcher fetcher,
    Position? upToRevision,
    bool resetCache) =>
    Fetch(id, upToRevision, fetcher, resetCache).Map(fr => fr.Ent.Map(e => (T)(object)e)).Async();

  public Task<FetchResult<Entity>> Fetch(
    Option<StrongId> id,
    Position? upToRevision,
    Fetcher fetcher,
    bool resetCache = false) =>
    fetch(id, upToRevision, fetcher, resetCache);

  private static Func<Option<StrongId>, Position?, Fetcher, bool, Task<FetchResult<Entity>>> Build(
    EventStoreClient client,
    Func<StrongId, Option<Entity>> defaulter,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    MemoryCache cache,
    TimeSpan cacheExpiration,
    bool isSlidingCache)
  {
    var interestFetcher = new InterestFetcher(client, parser);
    var entryOptions = new MemoryCacheEntryOptions { Size = 1 };
    if (isSlidingCache)
    {
      entryOptions.SlidingExpiration = cacheExpiration;
    }
    else
    {
      entryOptions.AbsoluteExpirationRelativeToNow = cacheExpiration;
    }

    return (id, r, f, sk) => id
      .Bind(defaulter)
      .Match(
        e => Fetch(e, r, f, sk),
        () => new FetchResult<Entity>(None, -1, None, null, null, null, null).Apply(Task.FromResult));

    async Task<FetchResult<Entity>> Fetch(Entity defaulted, Position? upToRevision, Fetcher fetcher, bool resetCache)
    {
      var interests = (await interestFetcher.Interests(defaulted.GetStreamName())).Select(i => i.StreamName).ToArray();

      if (resetCache)
      {
        cache.Remove(defaulted.GetStreamName());
      }

      return interests.Length == 0 ? await FromSingleStream() : await FromConcernedStreams();

      async Task<FetchResult<Entity>> FromConcernedStreams()
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
              new RevisionFetchWrapper(fetcher, re.OriginalEvent.Position));

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

      async Task<FetchResult<Entity>> FromSingleStream()
      {
        var cached = resetCache ? new Miss() : cache.Find(defaulted.GetStreamName());
        (Entity e, Option<long> r, Option<Position> gp, DateTime? fe, DateTime? le, string? fu, string? lu) seed =
          upToRevision is null
            ? cached switch
            {
              SingleStreamCacheResult<Entity> single =>
              (
                single.Entity,
                single.Revision,
                single.GlobalPosition,
                single.FirstEventAt,
                single.LastEventAt,
                single.FirstUserSubFound,
                single.LastUserSubFound),
              _ => (defaulted, None, None, null, null, null, null)
            }
            : (defaulted, None, None, null, null, null, null);

        var read = client.ReadStreamAsync(
          Direction.Forwards,
          seed.e.GetStreamName(),
          seed.r.Match(r => StreamPosition.FromInt64(r + 1), () => StreamPosition.Start));

        if (await read.ReadState == ReadState.StreamNotFound)
        {
          return new FetchResult<Entity>(None, -1, None, null, null, null, null);
        }

        var result = await read
          .TakeWhile(re => upToRevision is null || re.Event.Position <= upToRevision)
          .AggregateAwaitAsync<
            ResolvedEvent,
            (Entity entity, long rev, Option<Position> gp, DateTime? fe, DateTime? le, string? fu, string? lu),
            FetchResult<Entity>>(
            (seed.e, seed.r.DefaultValue(-1), seed.gp, seed.fe, seed.le, seed.fu, seed.lu),
            async (r, @event) =>
              await parser(@event)
                .Match<ValueTask<(Entity entity, long rev, Option<Position> gp, DateTime? fe, DateTime? le, string? fu,
                  string? lu)>>(
                  async evt =>
                  {
                    var metadata = EventMetadata.TryParse(@event);
                    var firstEventAt = r.fe ?? metadata.CreatedAt;
                    var lastEventAt = metadata.CreatedAt;
                    var firstUserSubFound = r.fu ?? metadata.RelatedUserSub;
                    var lastUserSubFound = metadata.RelatedUserSub ?? r.lu;
                    return (
                      await r.entity.Fold(
                        evt,
                        metadata,
                        new RevisionFetchWrapper(fetcher, @event.OriginalEvent.Position)),
                      @event.Event.EventNumber.ToInt64(),
                      Some(@event.Event.Position),
                      firstEventAt,
                      lastEventAt,
                      firstUserSubFound,
                      lastUserSubFound);
                  },
                  () =>
                  {
                    var (createdAt, _, _, relatedUserSub, _) = EventMetadata.TryParse(@event);
                    DateTime? firstEventAt = r.fe ?? createdAt;
                    DateTime? lastEventAt = createdAt;
                    var firstUserSubFound = r.fu ?? relatedUserSub;
                    var lastUserSubFound = relatedUserSub ?? r.lu;
                    return ValueTask.FromResult(
                      (
                        r.entity,
                        @event.Event.EventNumber.ToInt64(),
                        Some(@event.Event.Position),
                        firstEventAt,
                        lastEventAt,
                        firstUserSubFound,
                        lastUserSubFound));
                  }),
            tuple => ValueTask.FromResult(
              new FetchResult<Entity>(tuple.entity, tuple.rev, tuple.gp, tuple.fe, tuple.le, tuple.fu, tuple.lu)));

        if (result.Revision < 0 || upToRevision is not null)
        {
          return result;
        }

        foreach (var entity in result.Ent)
        {
          cache.Set(
            entity.GetStreamName(),
            new SingleStreamCacheResult<Entity>(
              entity,
              result.Revision,
              result.GlobalPosition,
              result.FirstEventAt ?? DateTime.UtcNow,
              result.LastEventAt ?? DateTime.UtcNow,
              result.FirstUserSubFound,
              result.LastUserSubFound),
            entryOptions);
        }

        return result;
      }
    }
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
}

internal class StreamZipWrapper(EventStoreClient.ReadStreamResult stream)
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

public record FetchResult<Entity>(
  Option<Entity> Ent,
  long Revision,
  Option<Position> GlobalPosition,
  DateTime? FirstEventAt,
  DateTime? LastEventAt,
  string? FirstUserSubFound,
  string? LastUserSubFound) where Entity : EventModelEntity<Entity>;

internal interface CacheResult;

internal record SingleStreamCacheResult<TEntity>(
  TEntity Entity,
  Option<long> Revision,
  Option<Position> GlobalPosition,
  DateTime FirstEventAt,
  DateTime LastEventAt,
  string? FirstUserSubFound,
  string? LastUserSubFound) : CacheResult;

internal record MultipleStreamCacheResult<TEntity>(
  TEntity Entity,
  Option<Position> GlobalPosition,
  Option<long> Revision,
  Dictionary<string, long> StreamRevisions,
  DateTime FirstEventAt,
  DateTime LastEventAt,
  string? FirstUserSubFound,
  string? LastUserSubFound) : CacheResult;

internal record Miss : CacheResult;

internal static class EntityCacheExtensions
{
  internal static CacheResult Find(this MemoryCache cache, string streamName) =>
    cache.TryGetValue<CacheResult>(streamName, out var value) && value is not null
      ? value
      : new Miss();
}
