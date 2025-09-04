using Nvx.ConsistentAPI;

namespace TestEventModel;

public static class MultiTenantModel
{
  public const string Permission = "test-multi-tenant-permission";

  public static readonly EventModel Model = new()
  {
    Entities =
    [
      new EntityDefinition<MultiTenantEntity, MultiTenantId>
      {
        Defaulter = MultiTenantEntity.Defaulted,
        StreamPrefix = MultiTenantEntity.StreamPrefix
      },

    ],
    ReadModels = [MultiTenantEntityReadModel.Definition]
  };
}

public record MultiTenantId(Guid Id) : StrongId
{
  public override string StreamId() => Id.ToString();
  public override string ToString() => StreamId();
}

public partial record MultiTenantEntity(Guid Id, Guid[] Tenants) : EventModelEntity<MultiTenantEntity>,
  Folds<MultiTenantEntityReceivedTenant, MultiTenantEntity>
{
  public const string StreamPrefix = "multi-tenant-entity-";

  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<MultiTenantEntity> Fold(
    MultiTenantEntityReceivedTenant evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this with { Tenants = [..Tenants, evt.TenantId] });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{new MultiTenantId(id)}";

  public static MultiTenantEntity Defaulted(MultiTenantId id) => new(id.Id, []);
}

public record MultiTenantEntityReceivedTenant(Guid Id, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => MultiTenantEntity.GetStreamName(Id);

  public StrongId GetEntityId() => new MultiTenantId(Id);
}

public record MultiTenantEntityReadModel(string Id, Guid EntityId, Guid[] TenantIds) : MultiTenantReadModel
{
  public static readonly EventModelingReadModelArtifact Definition =
    new ReadModelDefinition<MultiTenantEntityReadModel, MultiTenantEntity>
    {
      StreamPrefix = MultiTenantEntity.StreamPrefix,
      Projector = e => [new MultiTenantEntityReadModel(e.Id.ToString(), e.Id, e.Tenants)],
      AreaTag = "MultiTenantTesting",
      Auth = new PermissionsRequireAll(MultiTenantModel.Permission)
    };

  public StrongId GetStrongId() => new MultiTenantId(EntityId);
}
