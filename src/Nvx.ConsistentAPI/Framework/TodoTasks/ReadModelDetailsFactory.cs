namespace Nvx.ConsistentAPI;

public interface ReadModelDetailsFactory
{
  public TableDetails GetTableDetails<ReadModel>() where ReadModel : EventModelReadModel;
}
