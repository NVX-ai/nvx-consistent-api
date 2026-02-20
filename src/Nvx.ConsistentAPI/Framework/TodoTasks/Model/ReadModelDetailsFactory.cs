namespace Nvx.ConsistentAPI;

/// <summary>
/// Abstraction for retrieving table metadata (name, SQL statements, columns) for a given read model type.
/// </summary>
public interface ReadModelDetailsFactory
{
  TableDetails GetTableDetails<ReadModel>() where ReadModel : EventModelReadModel;
}
