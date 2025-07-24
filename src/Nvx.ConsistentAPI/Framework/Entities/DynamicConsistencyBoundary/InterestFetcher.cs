using EventStore.Client;
using Microsoft.Extensions.Caching.Memory;

namespace Nvx.ConsistentAPI;

internal class InterestFetcher(EventStoreClient client, Func<ResolvedEvent, Option<EventModelEvent>> parser)
{
  private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 500 });

  public async Task<Option<ConcernedEntityEntity>> Concerned(string streamName)
  {
    var entity = cache.Get(streamName) as InterestCacheElement<ConcernedEntityEntity>
                 ?? new InterestCacheElement<ConcernedEntityEntity>(
                   ConcernedEntityEntity.Defaulted(new ConcernedEntityId(streamName)),
                   -1);
    var position = entity.Revision == -1 ? StreamPosition.Start : StreamPosition.FromInt64(entity.Revision);
    var read = client.ReadStreamAsync(Direction.Forwards, entity.Entity.GetStreamName(), position);
    if (await read.ReadState == ReadState.StreamNotFound)
    {
      return None;
    }

    await foreach (var resolvedEvent in read)
    {
      foreach (var parsed in parser(resolvedEvent))
      {
        var metadata = EventMetadata.TryParse(
          resolvedEvent.Event.Metadata.ToArray(),
          resolvedEvent.Event.Created,
          resolvedEvent.Event.Position.CommitPosition,
          resolvedEvent.Event.EventNumber.ToUInt64());
        entity = new InterestCacheElement<ConcernedEntityEntity>(
          await entity.Entity.Fold(parsed, metadata, null!),
          resolvedEvent.Event.EventNumber.ToInt64());
      }
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

  public async Task<Option<InterestedEntityEntity>> Interested(string streamName)
  {
    var entity = cache.Get(streamName) as InterestCacheElement<InterestedEntityEntity>
                 ?? new InterestCacheElement<InterestedEntityEntity>(
                   InterestedEntityEntity.Defaulted(new InterestedEntityId(streamName)),
                   -1);
    var position = entity.Revision == -1 ? StreamPosition.Start : StreamPosition.FromInt64(entity.Revision);
    var read = client.ReadStreamAsync(Direction.Forwards, entity.Entity.GetStreamName(), position);
    if (await read.ReadState == ReadState.StreamNotFound)
    {
      return None;
    }

    await foreach (var resolvedEvent in read)
    {
      foreach (var parsed in parser(resolvedEvent))
      {
        var metadata = EventMetadata.TryParse(
          resolvedEvent.Event.Metadata.ToArray(),
          resolvedEvent.Event.Created,
          resolvedEvent.Event.Position.CommitPosition,
          resolvedEvent.Event.EventNumber.ToUInt64());
        entity = new InterestCacheElement<InterestedEntityEntity>(
          await entity.Entity.Fold(parsed, metadata, null!),
          resolvedEvent.Event.EventNumber.ToInt64());
      }
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
