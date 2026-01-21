using KurrentDB.Client;

namespace Nvx.ConsistentAPI.Framework.Entities;

public interface EntityDefinition
{
  string StreamPrefix { get; }

  EntityFetcher GetFetcher(
    KurrentDBClient client,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    InterestFetcher interestFetcher);
}

public class EntityDefinition<EntityShape, EntityId> :
  EntityDefinition
  where EntityShape : EventModelEntity<EntityShape>
  where EntityId : StrongId
{
  public required Func<EntityId, EntityShape> Defaulter { private get; init; }
  public int CacheSize { get; init; } = 4096;
  public TimeSpan CacheExpiration { get; init; } = TimeSpan.FromMinutes(30);
  public bool IsSlidingCache { get; init; } = true;
  public required string StreamPrefix { get; init; }

  public EntityFetcher GetFetcher(
    KurrentDBClient client,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    InterestFetcher interestFetcher) =>
    new Fetcher<EntityShape>(
      client,
      sid => Optional(sid as EntityId).Bind<EntityShape>(eid => Defaulter(eid)),
      parser,
      CacheSize,
      CacheExpiration,
      IsSlidingCache,
      StreamPrefix,
      interestFetcher);
}
