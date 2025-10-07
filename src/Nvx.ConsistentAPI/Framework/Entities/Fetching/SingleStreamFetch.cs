using EventStore.Client;
using Microsoft.Extensions.Caching.Memory;

namespace Nvx.ConsistentAPI;

internal static class SingleStreamFetch
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
    CancellationToken cancellationToken) where Entity : EventModelEntity<Entity>
  {
    var cached = resetCache ? new Miss() : cache.Find(defaulted.GetStreamName());
    (Entity e, Option<long> r, Option<Position> gp, ulong fep, ulong lep, DateTime? fe, DateTime? le, string? fu, string? lu) seed =
      upToRevision is null
        ? cached switch
        {
          SingleStreamCacheResult<Entity> single =>
          (
            single.Entity,
            single.Revision,
            single.GlobalPosition,
            single.FirstEventPosition,
            single.LastEventPosition,
            single.FirstEventAt,
            single.LastEventAt,
            single.FirstUserSubFound,
            single.LastUserSubFound),
          _ => (defaulted, None, None, 0, 0, null, null, null, null)
        }
        : (defaulted, None, None, 0, 0, null, null, null, null);

    var read = client.ReadStreamAsync(
      Direction.Forwards,
      seed.e.GetStreamName(),
      seed.r.Match(r => StreamPosition.FromInt64(r + 1), () => StreamPosition.Start),
      cancellationToken: cancellationToken);

    if (await read.ReadState == ReadState.StreamNotFound)
    {
      return new FetchResult<Entity>(None, -1, None, 0, 0, null, null, null, null);
    }

    var result = await read
      .TakeWhile(re => upToRevision is null || re.Event.Position <= upToRevision)
      .AggregateAwaitAsync<
        ResolvedEvent,
        (Entity entity, long rev, Option<Position> gp, ulong fep, ulong lep, DateTime? fe, DateTime? le, string? fu, string? lu),
        FetchResult<Entity>>(
        (seed.e, seed.r.DefaultValue(-1), seed.gp, seed.fep, seed.lep, seed.fe, seed.le, seed.fu, seed.lu),
        async (acc, @event) =>
          await parser(@event)
            .Match<ValueTask<(Entity entity, long rev, Option<Position> gp, ulong fep, ulong lep, DateTime? fe, DateTime? le, string? fu,
              string? lu)>>(
              async evt =>
              {
                var metadata = EventMetadata.TryParse(@event);
                var firstEventPosition = acc.fep == 0 ? @event.Event.Position.CommitPosition : acc.fep;
                var lastEventPosition = @event.Event.Position.CommitPosition;
                var firstEventAt = acc.fe ?? metadata.CreatedAt;
                var lastEventAt = metadata.CreatedAt;
                var firstUserSubFound = acc.fu ?? metadata.RelatedUserSub;
                var lastUserSubFound = metadata.RelatedUserSub ?? acc.lu;
                return (
                  await acc.entity.Fold(
                    evt,
                    metadata,
                    new RevisionFetchWrapper(fetcher, @event.OriginalEvent.Position, resetCache)),
                  @event.Event.EventNumber.ToInt64(),
                  Some(@event.Event.Position),
                  firstEventPosition,
                  lastEventPosition,
                  firstEventAt,
                  lastEventAt,
                  firstUserSubFound,
                  lastUserSubFound);
              },
              () =>
              {
                var (createdAt, _, _, relatedUserSub, _) = EventMetadata.TryParse(@event);
                var firstEventPosition = acc.fep == 0 ? @event.Event.Position.CommitPosition : acc.fep;
                var lastEventPosition = @event.Event.Position.CommitPosition;
                DateTime? firstEventAt = acc.fe ?? createdAt;
                DateTime? lastEventAt = createdAt;
                var firstUserSubFound = acc.fu ?? relatedUserSub;
                var lastUserSubFound = relatedUserSub ?? acc.lu;
                return ValueTask.FromResult(
                  (
                    acc.entity,
                    acc.rev,
                    acc.gp,
                    firstEventPosition,
                    lastEventPosition,
                    firstEventAt,
                    lastEventAt,
                    firstUserSubFound,
                    lastUserSubFound));
              }),
        tuple => ValueTask.FromResult(
          new FetchResult<Entity>(tuple.entity, tuple.rev, tuple.gp, tuple.fep, tuple.lep, tuple.fe, tuple.le, tuple.fu, tuple.lu)),
        cancellationToken);

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
          result.FirstEventPosition,
          result.LastEventPosition,
          result.FirstEventAt ?? DateTime.UtcNow,
          result.LastEventAt ?? DateTime.UtcNow,
          result.FirstUserSubFound,
          result.LastUserSubFound),
        entryOptions);
    }

    return result;
  }
}
