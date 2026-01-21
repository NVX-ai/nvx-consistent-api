using Nvx.ConsistentAPI;

namespace Nvx.ConsistentAPI.Framework.Entities;

public interface EventModelEntity;

public interface EventModelEntity<Entity> : EventModelEntity
{
  ValueTask<Entity> Fold(EventModelEvent sa, EventMetadata metadata, RevisionFetcher fetcher);
  string GetStreamName();
}
