namespace Nvx.ConsistentAPI;

public interface MultiTenantReadModel : EventModelReadModel
{
  Guid[] TenantIds { get; }
}
