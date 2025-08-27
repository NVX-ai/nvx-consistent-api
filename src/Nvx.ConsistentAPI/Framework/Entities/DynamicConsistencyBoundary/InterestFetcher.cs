using EventStore.Client;
using Microsoft.Extensions.Caching.Memory;

namespace Nvx.ConsistentAPI;

internal record Concern(string StreamName, StrongId Id);
internal record Interest(string StreamName, StrongId Id);

internal class InterestFetcher(EventStoreClient client, Func<ResolvedEvent, Option<EventModelEvent>> parser)
{
  private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 500 });

  public async Task<Concern[]> Concerns(string streamName)
  {
    var concerns = new List<Concern>();
    while (true)
    {
      var newConcerns = (await Concerned(streamName)
          .Async()
          .Map(ce => ce.InterestedStreams.Choose(t => t.id.GetStrongId().Map(id => new Concern(t.name, id))))
          .DefaultValue([]))
        .Where(nc => concerns.All(c => c.StreamName != nc.StreamName))
        .ToArray();
      if (newConcerns.Length == 0)
      {
        break;
      }

      concerns.AddRange(newConcerns);
      foreach (var newConcern in newConcerns)
      {
        concerns.AddRange(await Concerns(newConcern.StreamName));
      }
    }

    return concerns.ToArray();
  }

  private async Task<Option<ConcernedEntityEntity>> Concerned(string streamName)
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
        var metadata = EventMetadata.TryParse(resolvedEvent);
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

  public async Task<Interest[]> Interests(string streamName)
  {
    var interests = new List<Interest>();
    while (true)
    {
      var newInterests = (await Interested(streamName)
          .Async()
          .Map(ie => ie.ConcernedStreams.Choose(t => t.id.GetStrongId().Map(id => new Interest(t.name, id))))
          .DefaultValue([]))
        .Where(ni => interests.All(i => i.StreamName != ni.StreamName))
        .ToArray();
      if (newInterests.Length == 0)
      {
        break;
      }

      interests.AddRange(newInterests);
      foreach (var newInterest in newInterests)
      {
        interests.AddRange(await Interests(newInterest.StreamName));
      }
    }

    return interests.ToArray();
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
        var metadata = EventMetadata.TryParse(resolvedEvent);
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
