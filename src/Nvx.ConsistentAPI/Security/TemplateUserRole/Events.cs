namespace Nvx.ConsistentAPI;

public record TemplateUserRoleDescribed(Guid TemplateUserRoleId, string Name, string Description) : EventModelEvent
{
  public string GetSwimlane() => TemplateUserRoleEntity.StreamPrefix;
  public StrongId GetEntityId() => new TemplateUserRoleId(TemplateUserRoleId);
}

public record TemplateUserRoleUpdated(
  Guid TemplateUserRoleId,
  string Name,
  string Description) : EventModelEvent
{
  public string GetSwimlane() => TemplateUserRoleEntity.StreamPrefix;
  public StrongId GetEntityId() => new TemplateUserRoleId(TemplateUserRoleId);
}

public record TemplateUserRoleRemoved(Guid TemplateUserRoleId) : EventModelEvent
{
  public string GetSwimlane() => TemplateUserRoleEntity.StreamPrefix;
  public StrongId GetEntityId() => new TemplateUserRoleId(TemplateUserRoleId);
}

public record TemplateUserRolePermissionAdded(Guid TemplateUserRoleId, string Permission) : EventModelEvent
{
  public string GetSwimlane() => TemplateUserRoleEntity.StreamPrefix;
  public StrongId GetEntityId() => new TemplateUserRoleId(TemplateUserRoleId);
}

public record TemplateUserRolePermissionRemoved(Guid TemplateUserRoleId, string Permission) : EventModelEvent
{
  public string GetSwimlane() => TemplateUserRoleEntity.StreamPrefix;
  public StrongId GetEntityId() => new TemplateUserRoleId(TemplateUserRoleId);
}
