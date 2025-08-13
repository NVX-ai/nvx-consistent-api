using EventStore.Client;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Nvx.ConsistentAPI.Store.Events.Metadata;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI;

public interface EntityFetcher
{
  Task<FetchResult<T>> Fetch<T>(Option<StrongId> id, ulong? upToRevision, Fetcher fetcher)
    where T : EventModelEntity<T>;

  internal AsyncOption<FoundEntity> DaemonFetch(Option<StrongId> id, Fetcher fetcher, bool resetCache = false);

  Type GetFetchType();

  internal bool CanProcessStream(string streamName);
  internal Option<ulong> GetCachedStreamRevision(StrongId id);
  internal Option<ulong> GetCachedGlobalPosition(StrongId id);
}

public interface RevisionFetcher
{
  AsyncOption<T> Fetch<T>(Option<StrongId> id) where T : EventModelEntity<T>;
  AsyncOption<T> LatestFetch<T>(Option<StrongId> id) where T : EventModelEntity<T>;
}

internal class RevisionFetchWrapper(Fetcher fetcher, ulong upToRevision) : RevisionFetcher
{
  public AsyncOption<T> Fetch<T>(Option<StrongId> id) where T : EventModelEntity<T> =>
    fetcher.Fetch<T>(id, upToRevision).Map(fr => fr.Ent);

  public AsyncOption<T> LatestFetch<T>(Option<StrongId> id) where T : EventModelEntity<T> =>
    fetcher.Fetch<T>(id).Map(fr => fr.Ent);
}

public class Fetcher
{
  private readonly EntityFetcher[] fetchers;

  internal Fetcher(IEnumerable<EntityFetcher> fetchers)
  {
    this.fetchers = fetchers.ToArray();
  }

  internal Option<ulong> GetCachedStreamRevision(StrongId id) =>
    fetchers
      .SingleOrNone(f => f.CanProcessStream(id.StreamId()))
      .Bind(f => f.GetCachedStreamRevision(id));

  internal Option<Position> GetCachedGlobalPosition(StrongId id) =>
    fetchers
      .SingleOrNone(f => f.CanProcessStream(id.StreamId()))
      .Bind(f => f.GetCachedGlobalPosition(id));

  internal AsyncOption<FoundEntity> DaemonFetch(Option<StrongId> id, string streamName, bool resetCache = false) =>
    fetchers
      .SingleOrNone(f => f.CanProcessStream(streamName))
      .Match(
        f => f.DaemonFetch(id, this, resetCache),
        () => Option<FoundEntity>.None.ToTask());

  public Task<FetchResult<T>> Fetch<T>(Option<StrongId> id, ulong? upToRevision = null)
    where T : EventModelEntity<T> =>
    fetchers
      .SingleOrNone(f => f.GetFetchType() == typeof(T))
      .Match(f => f.Fetch<T>(id, upToRevision, this), () => throw new InvalidOperationException());

  public AsyncResult<FetchResult<T>, ApiError> SafeFetch<T>(
    Option<StrongId> id,
    ulong? upToRevision = null) where T : EventModelEntity<T>
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

  private readonly Func<Option<StrongId>, ulong?, Fetcher, bool, Task<FetchResult<Entity>>> fetch;

  private readonly string streamPrefix;

  public Fetcher(
    EventStoreClient client,
    EventStore<EventModelEvent> store,
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
      store,
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

  public Option<ulong> GetCachedStreamRevision(StrongId id) =>
    cache.TryGetValue<CacheResult>(id.StreamId(), out var cachedEntity)
      ? cachedEntity switch
      {
        SingleStreamCacheResult<Entity> single => single.Revision,
        MultipleStreamCacheResult<Entity> multiple =>
          multiple.StreamRevisions.TryGetValue(id.StreamId(), out var revision) ? Some(revision) : None,
        _ => None
      }
      : None;

  public Option<Position> GetCachedGlobalPosition(StrongId id) =>
    cache.TryGetValue<CacheResult>(id.StreamId(), out var cachedEntity)
      ? cachedEntity switch
      {
        MultipleStreamCacheResult<Entity> multiple => multiple.GlobalPosition,
        _ => None
      }
      : None;

  public Task<FetchResult<T>> Fetch<T>(Option<StrongId> id, ulong? upToRevision, Fetcher fetcher)
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

  public Task<FetchResult<Entity>> Fetch(
    Option<StrongId> id,
    ulong? upToRevision,
    Fetcher fetcher,
    bool resetCache = false) =>
    fetch(id, upToRevision, fetcher, resetCache);

  private static Func<Option<StrongId>, ulong?, Fetcher, bool, Task<FetchResult<Entity>>> Build(
    EventStoreClient client,
    EventStore<EventModelEvent> store,
    Func<StrongId, Option<Entity>> defaulter,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    MemoryCache cache,
    TimeSpan cacheExpiration,
    bool isSlidingCache)
  {
    var interestFetcher = new InterestFetcher(store);
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
      .Bind(i => defaulter(i).Map(e => (e, i)))
      .Match(
        t => Fetch(t.e, t.i, r, f, sk),
        () => new FetchResult<Entity>(None, -1, None, null, null, null, null).Apply(Task.FromResult));

    async Task<FetchResult<Entity>> Fetch(
      Entity defaulted,
      StrongId id,
      ulong? upToGlobalPosition,
      Fetcher fetcher,
      bool resetCache)
    {
      var interests = await interestFetcher
        .Interested(defaulted.GetStreamName())
        .Async()
        .Match(i => i.ConcernedStreams, () => []);

      if (resetCache)
      {
        cache.Remove(defaulted.GetStreamName());
      }

      return interests.Length == 0 ? await FromSingleStream() : await FromConcernedStreams();

      async Task<FetchResult<Entity>> FromConcernedStreams()
      {
        var cached = resetCache ? new Miss() : cache.Find(defaulted.GetStreamName());
        var allStreams = interests
          .Append((defaulted.GetStreamName(), id))
          .Distinct()
          .ToArray();

        var revisions = cached is MultipleStreamCacheResult<Entity> m
          ? m.StreamRevisions
          : new Dictionary<string, ulong>();

        (Entity e, Option<Position> gp, Option<ulong> r, DateTime? fe, DateTime? le, string? fu, string? lu) seed =
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
        await foreach (var @event in Zip(allStreams, store, revisions))
        {
          if (@event.Event.GetStreamName() == seed.e.GetStreamName() && @event.Metadata.GlobalPosition > upToGlobalPosition)
          {
            continue;
          }

          hadEvents = true;
          revisions[@event.Event.GetStreamName()] = @event.Metadata.GlobalPosition;

          var metadata =
            new EventMetadata(
              @event.Metadata.CreatedAt,
              @event.Metadata.CorrelationId,
              @event.Metadata.CausationId,
              @event.Metadata.RelatedUserSub,
              @event.Metadata.GlobalPosition,
              @event.Metadata.StreamPosition);

          var firstEventAt = seed.fe ?? metadata.CreatedAt;
          var lastEventAt = metadata.CreatedAt;
          var firstUserSubFound = seed.fu ?? metadata.RelatedUserSub;
          var lastUserSubFound = metadata.RelatedUserSub ?? seed.lu;

          var folded = await seed.e.Fold(
            @event.Event,
            metadata,
            new RevisionFetchWrapper(fetcher, @event.Metadata.GlobalPosition));

          var revision = @event.Event.GetStreamName() == seed.e.GetStreamName()
            ? @event.Metadata.StreamPosition
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

        if (hadEvents && upToGlobalPosition is null)
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
          upToGlobalPosition is null
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
          .TakeWhile(re => upToGlobalPosition is null || re.Event.Position <= upToGlobalPosition)
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
                    var metadata = EventMetadata.TryParse(
                      @event.Event.Metadata.ToArray(),
                      @event.Event.Created,
                      @event.Event.Position.CommitPosition,
                      @event.Event.EventNumber.ToUInt64());
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
                    var (createdAt, _, _, relatedUserSub, _, _) = EventMetadata.TryParse(
                      @event.Event.Metadata.ToArray(),
                      @event.Event.Created,
                      @event.Event.Position.CommitPosition,
                      @event.Event.EventNumber.ToUInt64());
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

        if (result.Revision < 0 || upToGlobalPosition is not null)
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

  private static async IAsyncEnumerable<ReadStreamMessage<EventModelEvent>.SolvedEvent> Zip(
    (string name, StrongId id)[] streams,
    EventStore<EventModelEvent> store,
    Dictionary<string, ulong> streamRevisions)
  {
    var zipWrappers = streams
      .Select(s =>
      {
        var request = streamRevisions.TryGetValue(s.name, out var r)
          ? ReadStreamRequest.FromAndAfter(s.name, s.id, r + 1)
          : ReadStreamRequest.Forwards(s.name, s.id);

        return new StreamZipWrapper(store.Read(request));
      })
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

internal class StreamZipWrapper(IAsyncEnumerable<ReadStreamMessage<EventModelEvent>> stream)
{
  private readonly IAsyncEnumerator<ReadStreamMessage<EventModelEvent>.SolvedEvent> enumerator =
    stream.Events().GetAsyncEnumerator();

  private ReadStreamMessage<EventModelEvent>.SolvedEvent? current;
  public bool IsDone { get; private set; }

  public GlobalPosition? Position =>
    current is not null
      ? new GlobalPosition(current.Metadata.GlobalPosition, current.Metadata.GlobalPosition)
      : null;

  public ReadStreamMessage<EventModelEvent>.SolvedEvent? TryPop()
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
  Option<ulong> Revision,
  Option<Position> GlobalPosition,
  DateTime FirstEventAt,
  DateTime LastEventAt,
  string? FirstUserSubFound,
  string? LastUserSubFound) : CacheResult;

internal record MultipleStreamCacheResult<TEntity>(
  TEntity Entity,
  Option<Position> GlobalPosition,
  Option<ulong> Revision,
  Dictionary<string, ulong> StreamRevisions,
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
