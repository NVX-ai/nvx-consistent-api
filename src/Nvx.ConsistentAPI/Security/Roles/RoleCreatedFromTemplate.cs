namespace Nvx.ConsistentAPI;

public record RoleCreatedFromTemplate(Guid Id, Guid TemplateId, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => RoleEntity.GetStreamName(new RoleId(Id, TenantId));

  public StrongId GetEntityId() => new RoleId(Id, TenantId);
}
