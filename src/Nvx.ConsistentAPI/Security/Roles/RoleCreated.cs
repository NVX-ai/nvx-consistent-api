namespace Nvx.ConsistentAPI;

public record RoleCreated(Guid Id, string Name, string Description, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => RoleEntity.GetStreamName(new RoleId(Id, TenantId));

  public StrongId GetEntityId() => new RoleId(Id, TenantId);
}
