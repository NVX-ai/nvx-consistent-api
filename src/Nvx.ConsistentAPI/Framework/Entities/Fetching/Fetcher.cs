using EventStore.Client;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace Nvx.ConsistentAPI;

public interface EntityFetcher
{
  Task<FetchResult<T>> Fetch<T>(
    Option<StrongId> id,
    Position? upToRevision,
    Fetcher fetcher,
    CancellationToken cancellationToken = default)
    where T : EventModelEntity<T>;

  internal AsyncOption<T> WrappedFetch<T>(Option<StrongId> id, Fetcher fetcher, Position? upToRevision, bool resetCache)
    where T : EventModelEntity<T>;

  internal AsyncOption<FoundEntity> DaemonFetch(
    Option<StrongId> id,
    Fetcher fetcher,
    bool resetCache = false,
    CancellationToken cancellationToken = default);

  Type GetFetchType();

  internal bool CanProcessStream(string streamName);
  internal Option<long> GetCachedStreamRevision(StrongId id);
  internal Option<ulong> GetCachedLastPosition(StrongId id);
}

public interface RevisionFetcher
{
  AsyncOption<T> Fetch<T>(Option<StrongId> id) where T : EventModelEntity<T>;
  AsyncOption<T> LatestFetch<T>(Option<StrongId> id) where T : EventModelEntity<T>;
}

internal class RevisionFetchWrapper(Fetcher fetcher, Position upToRevision, bool resetCache) : RevisionFetcher
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

  internal Option<ulong> GetCachedLastPosition(string streamName, StrongId id) =>
    fetchers
      .SingleOrNone(f => f.CanProcessStream(streamName))
      .Bind(f => f.GetCachedLastPosition(id))
      .Map(r => r);

  internal AsyncOption<T> WrappedFetch<T>(Option<StrongId> id, Position? upToRevision, bool resetCache)
    where T : EventModelEntity<T> =>
    fetchers
      .SingleOrNone(f => f.GetFetchType() == typeof(T))
      .Match(f => f.WrappedFetch<T>(id, this, upToRevision, resetCache), () => throw new InvalidOperationException());


  internal AsyncOption<FoundEntity> DaemonFetch(
    Option<StrongId> id,
    string streamName,
    bool resetCache = false,
    CancellationToken cancellationToken = default) =>
    fetchers
      .SingleOrNone(f => f.CanProcessStream(streamName))
      .Match(
        f => f.DaemonFetch(id, this, resetCache, cancellationToken),
        () => Option<FoundEntity>.None.ToTask());

  public Task<FetchResult<T>> Fetch<T>(
    Option<StrongId> id,
    Position? upToRevision = null,
    CancellationToken cancellationToken = default)
    where T : EventModelEntity<T> =>
    fetchers
      .SingleOrNone(f => f.GetFetchType() == typeof(T))
      .Match(f => f.Fetch<T>(id, upToRevision, this, cancellationToken), () => throw new InvalidOperationException());

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

  private readonly Func<Option<StrongId>, Position?, Fetcher, bool, CancellationToken, Task<FetchResult<Entity>>> fetch;

  private readonly string streamPrefix;

  public Fetcher(
    EventStoreClient client,
    Func<StrongId, Option<Entity>> defaulter,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    int cacheSize,
    TimeSpan cacheExpiration,
    bool isSlidingCache,
    string streamPrefix,
    InterestFetcher interestFetcher)
  {
    this.streamPrefix = streamPrefix;
    cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = cacheSize });
    fetch = Build(
      client,
      defaulter,
      parser,
      cache,
      cacheExpiration,
      isSlidingCache,
      interestFetcher);
  }

  public AsyncOption<FoundEntity> DaemonFetch(
    Option<StrongId> id,
    Fetcher fetcher,
    bool resetCache = false,
    CancellationToken cancellationToken = default) =>
    Fetch(id, null, fetcher, resetCache, cancellationToken)
      .Map(FoundEntity<Entity>.From)
      .Async()
      .Map(FoundEntity (fe) => fe);

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

  public Option<ulong> GetCachedLastPosition(StrongId id) =>
    cache.TryGetValue<CacheResult>(id.StreamId(), out var cachedEntity)
      ? cachedEntity switch
      {
        SingleStreamCacheResult<Entity> single => single.GlobalPosition.Map(p => p.CommitPosition),
        MultipleStreamCacheResult<Entity> multiple => multiple.GlobalPosition.Map(p => p.CommitPosition),
        _ => None
      }
      : None;

  public Task<FetchResult<T>> Fetch<T>(
    Option<StrongId> id,
    Position? upToRevision,
    Fetcher fetcher,
    CancellationToken cancellationToken)
    where T : EventModelEntity<T> =>
    Fetch(id, upToRevision, fetcher, cancellationToken: cancellationToken)
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
    bool resetCache = false,
    CancellationToken cancellationToken = default) =>
    fetch(id, upToRevision, fetcher, resetCache, cancellationToken);

  private static Func<Option<StrongId>, Position?, Fetcher, bool, CancellationToken, Task<FetchResult<Entity>>> Build(
    EventStoreClient client,
    Func<StrongId, Option<Entity>> defaulter,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    MemoryCache cache,
    TimeSpan cacheExpiration,
    bool isSlidingCache,
    InterestFetcher interestFetcher)
  {
    var entryOptions = new MemoryCacheEntryOptions { Size = 1 };
    if (isSlidingCache)
    {
      entryOptions.SlidingExpiration = cacheExpiration;
    }
    else
    {
      entryOptions.AbsoluteExpirationRelativeToNow = cacheExpiration;
    }

    return (id, r, f, sk, ct) => id
      .Bind(defaulter)
      .Match(
        e => Fetch(e, r, f, sk, ct),
        () => new FetchResult<Entity>(None, -1, None, null, null, null, null).Apply(Task.FromResult));

    async Task<FetchResult<Entity>> Fetch(
      Entity defaulted,
      Position? upToRevision,
      Fetcher fetcher,
      bool resetCache,
      CancellationToken cancellationToken)
    {
      var interests = (await interestFetcher.Interests(defaulted.GetStreamName())).Select(i => i.StreamName).ToArray();

      if (resetCache)
      {
        cache.Remove(defaulted.GetStreamName());
      }

      return interests.Length == 0
        ? await SingleStreamFetch.Do(
          cache,
          resetCache,
          defaulted,
          upToRevision,
          client,
          parser,
          entryOptions,
          fetcher,
          cancellationToken)
        : await MultiStreamFetch.Do(
          cache,
          resetCache,
          defaulted,
          upToRevision,
          client,
          parser,
          entryOptions,
          fetcher,
          interests,
          cancellationToken);
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
