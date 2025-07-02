namespace ConsistentAPI;

public interface FoldsExternally<Evt, Ent> where Evt : EventModelEvent where Ent : EventModelEntity<Ent>
{
  ValueTask<Ent> Fold(Evt evt, EventMetadata metadata, RevisionFetcher fetcher);
}
