using EventStore.Client;
using Microsoft.Extensions.Caching.Memory;

namespace Nvx.ConsistentAPI;

public interface InterestRelation
{
  string StreamName { get; }
}

public record Concern(string StreamName, StrongId Id) : InterestRelation;

public record Interest(string StreamName, StrongId Id) : InterestRelation;

public class InterestFetcher(EventStoreClient client, Func<ResolvedEvent, Option<EventModelEvent>> parser)
{
  private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 25_000 });

  private static async Task<TOut[]> Relations<TOut, TEntity>(
    string streamName,
    Func<string, Task<Option<TEntity>>> tryGet,
    Func<TEntity, IEnumerable<TOut>> mapper) where TOut : InterestRelation
  {
    var relations = new List<TOut>();
    while (true)
    {
      var newRelations = (await tryGet(streamName)
          .Async()
          .Map(mapper)
          .DefaultValue([]))
        .Distinct()
        .Where(nc => relations.All(c => c.StreamName != nc.StreamName))
        .ToArray();

      if (newRelations.Length == 0)
      {
        break;
      }

      relations.AddRange(newRelations);
      foreach (var newRelation in newRelations)
      {
        relations.AddRange(await Relations(newRelation.StreamName, tryGet, mapper));
      }
    }

    return relations.Distinct().ToArray();
  }

  public async Task<Concern[]> Concerns(string streamName) =>
    await Relations<Concern, ConcernedEntityEntity>(
      streamName,
      Concerned,
      ce => ce.InterestedStreams.Choose(t => t.id.GetStrongId().Map(id => new Concern(t.name, id))));

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

  public async Task<Interest[]> Interests(string streamName) =>
    await Relations<Interest, InterestedEntityEntity>(
      streamName,
      Interested,
      ie => ie.ConcernedStreams.Choose(t => t.id.GetStrongId().Map(id => new Interest(t.name, id))));

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
