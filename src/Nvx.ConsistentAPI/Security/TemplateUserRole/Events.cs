namespace Nvx.ConsistentAPI;

public record TemplateUserRoleDescribed(Guid TemplateUserRoleId, string Name, string Description) : EventModelEvent
{
  public string GetStreamName() => TemplateUserRoleEntity.GetStreamName(TemplateUserRoleId);

  public StrongId GetEntityId() => new TemplateUserRoleId(TemplateUserRoleId);
}

public record TemplateUserRoleUpdated(
  Guid TemplateUserRoleId,
  string Name,
  string Description) : EventModelEvent
{
  public string GetStreamName() => TemplateUserRoleEntity.GetStreamName(TemplateUserRoleId);

  public StrongId GetEntityId() => new TemplateUserRoleId(TemplateUserRoleId);
}

public record TemplateUserRoleRemoved(Guid TemplateUserRoleId) : EventModelEvent
{
  public string GetStreamName() => TemplateUserRoleEntity.GetStreamName(TemplateUserRoleId);

  public StrongId GetEntityId() => new TemplateUserRoleId(TemplateUserRoleId);
}

public record TemplateUserRolePermissionAdded(Guid TemplateUserRoleId, string Permission) : EventModelEvent
{
  public string GetStreamName() => TemplateUserRoleEntity.GetStreamName(TemplateUserRoleId);

  public StrongId GetEntityId() => new TemplateUserRoleId(TemplateUserRoleId);
}

public record TemplateUserRolePermissionRemoved(Guid TemplateUserRoleId, string Permission) : EventModelEvent
{
  public string GetStreamName() => TemplateUserRoleEntity.GetStreamName(TemplateUserRoleId);

  public StrongId GetEntityId() => new TemplateUserRoleId(TemplateUserRoleId);
}
