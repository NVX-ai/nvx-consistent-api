using EventStore.Client;

namespace Nvx.ConsistentAPI.InternalTooling;

public class ConsistencyCheck(
  string readModelsConnectionString,
  string modelHash,
  ReadModelHydrationDaemon daemon,
  EventModelingReadModelArtifact[] aggregatingReadModels,
  EventStoreClient eventStoreClient)
{
  public async Task<bool> IsConsistentAt(ulong position) =>
    (await Task.WhenAll(
      CentralDaemonIsConsistentAt(position),
      AggregatingConsistentAt(position),
      TodosProcessedAt(position))).All(Id);

  private async Task<bool> CentralDaemonIsConsistentAt(ulong position) => false;

  private async Task<bool> AggregatingConsistentAt(ulong position)
  {
    foreach (var readModel in aggregatingReadModels)
    {
      var insight = await readModel.Insights(position, eventStoreClient);
      if (!insight.IsCaughtUp)
      {
        return false;
      }
    }

    return true;
  }

  private async Task<bool> TodosProcessedAt(ulong position) => false;
}
