using Microsoft.Extensions.Caching.Memory;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI;

internal class InterestFetcher(EventStore<EventModelEvent> store)
{
  private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 500 });

  public async Task<Option<ConcernedEntityEntity>> Concerned(string streamName)
  {
    var entity = cache.Get(streamName) as InterestCacheElement<ConcernedEntityEntity>
                 ?? new InterestCacheElement<ConcernedEntityEntity>(
                   ConcernedEntityEntity.Defaulted(new ConcernedEntityId(streamName)),
                   -1);

    var request = entity.Revision == -1
      ? ReadStreamRequest.Forwards(ConcernedEntityEntity.StreamPrefix, new ConcernedEntityId(streamName))
      : ReadStreamRequest.FromAndAfter(
        ConcernedEntityEntity.StreamPrefix,
        new ConcernedEntityId(streamName),
        Convert.ToInt64(entity.Revision));
    var stream = store.Read(request);

    entity = await stream
      .Events()
      .AggregateAwaitAsync(
        entity,
        async (current, evt) => new InterestCacheElement<ConcernedEntityEntity>(
          await current.Entity.Fold(evt.Event, EventMetadata.From(evt.Metadata), null!),
          Convert.ToInt64(evt.Metadata.StreamPosition)));

    if (entity.Revision == -1)
    {
      return None;
    }

    cache.Set(
      streamName,
      entity,
      new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) });

    return entity.Entity;
  }

  public async Task<Option<InterestedEntityEntity>> Interested(string streamName)
  {
    var entity = cache.Get(streamName) as InterestCacheElement<InterestedEntityEntity>
                 ?? new InterestCacheElement<InterestedEntityEntity>(
                   InterestedEntityEntity.Defaulted(new InterestedEntityId(streamName)),
                   -1);

    var request = entity.Revision == -1
      ? ReadStreamRequest.Forwards(InterestedEntityEntity.StreamPrefix, new InterestedEntityId(streamName))
      : ReadStreamRequest.FromAndAfter(
        InterestedEntityEntity.StreamPrefix,
        new InterestedEntityId(streamName),
        Convert.ToInt64(entity.Revision));

    var stream = store.Read(request);
    await foreach (var evt in stream.Events())
    {
      var metadata = new EventMetadata(
        evt.Metadata.CreatedAt,
        evt.Metadata.CorrelationId,
        evt.Metadata.CausationId,
        evt.Metadata.RelatedUserSub,
        evt.Metadata.GlobalPosition,
        evt.Metadata.StreamPosition);

      entity = new InterestCacheElement<InterestedEntityEntity>(
        await entity.Entity.Fold(evt.Event, metadata, null!),
        Convert.ToInt64(evt.Metadata.StreamPosition));
    }

    if (entity.Revision == -1)
    {
      return None;
    }

    cache.Set(
      streamName,
      entity,
      new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) });

    return entity.Entity;
  }

  private record InterestCacheElement<T>(T Entity, long Revision);
}
