using EventStore.Client;

namespace Nvx.ConsistentAPI;

public interface Folds<Evt, Ent> where Evt : EventModelEvent where Ent : EventModelEntity<Ent>
{
  ValueTask<Ent> Fold(Evt evt, EventMetadata metadata, RevisionFetcher fetcher);
}

public record StrongString(string Value) : StrongId
{
  public override string StreamId() => Value;
  public override string ToString() => StreamId();
}

public record StrongGuid(Guid Value) : StrongId
{
  public override string StreamId() => Value.ToString();
  public override string ToString() => StreamId();
}

public interface EventModelEntity;

public interface EventModelEntity<Entity> : EventModelEntity
{
  ValueTask<Entity> Fold(EventModelEvent sa, EventMetadata metadata, RevisionFetcher fetcher);
  string GetStreamName();
}

public interface EntityDefinition
{
  string StreamPrefix { get; }
  EntityFetcher GetFetcher(EventStoreClient client, Func<ResolvedEvent, Option<EventModelEvent>> parser);
}

public class EntityDefinition<EntityShape, EntityId> :
  EntityDefinition
  where EntityShape : EventModelEntity<EntityShape>
  where EntityId : StrongId
{
  public required Func<EntityId, EntityShape> Defaulter { private get; init; }
  public int CacheSize { get; init; } = 1024;
  public TimeSpan CacheExpiration { get; init; } = TimeSpan.FromMinutes(30);
  public bool IsSlidingCache { get; init; } = true;
  public required string StreamPrefix { get; init; }

  public EntityFetcher GetFetcher(EventStoreClient client, Func<ResolvedEvent, Option<EventModelEvent>> parser) =>
    new Fetcher<EntityShape>(
      client,
      sid => Optional(sid as EntityId).Bind<EntityShape>(eid => Defaulter(eid)),
      parser,
      CacheSize,
      CacheExpiration,
      IsSlidingCache,
      StreamPrefix);
}
