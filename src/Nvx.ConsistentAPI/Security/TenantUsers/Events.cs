namespace Nvx.ConsistentAPI.TenantUsers;

public record UserWasAddedToTenant(Guid TenantId, string UserId) : EventModelEvent
{
  public string GetSwimlane() => TenantUsersEntity.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(TenantId);
}

public record UserWasRemovedFromTenant(Guid TenantId, string UserId) : EventModelEvent
{
  public string GetSwimlane() => TenantUsersEntity.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(TenantId);
}

public record TenantNameWasChanged(Guid TenantId, string NewName) : EventModelEvent
{
  public string GetSwimlane() => TenantUsersEntity.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(TenantId);
}
