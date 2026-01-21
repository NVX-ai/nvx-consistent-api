using Nvx.ConsistentAPI;

namespace Nvx.ConsistentAPI.Framework.Entities;

public interface Folds<Evt, Ent> where Evt : EventModelEvent where Ent : EventModelEntity<Ent>
{
  ValueTask<Ent> Fold(Evt evt, EventMetadata metadata, RevisionFetcher fetcher);
}
