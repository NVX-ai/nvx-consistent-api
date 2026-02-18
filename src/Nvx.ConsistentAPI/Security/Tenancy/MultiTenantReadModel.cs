namespace Nvx.ConsistentAPI;

public interface MultiTenantReadModel : EventModelReadModel
{
  public Guid[] TenantIds { get; }
}
