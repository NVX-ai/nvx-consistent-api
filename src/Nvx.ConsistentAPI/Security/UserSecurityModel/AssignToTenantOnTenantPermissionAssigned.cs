using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

public class AssignToTenantOnTenantPermissionAssigned :
  ProjectionDefinition<TenantPermissionAssigned, AddedToTenant, UserSecurity, UserSecurity, StrongString>
{
  public override string Name => "projection-tenant-permission-assigned-to-added-to-tenant";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<AddedToTenant> Project(
    TenantPermissionAssigned eventToProject,
    UserSecurity e,
    Option<UserSecurity> projectionEntity,
    StrongString projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    e.Tenants.Any(t => t.TenantId == eventToProject.TenantId)
      ? None
      : new AddedToTenant(e.Sub, eventToProject.TenantId);

  public override IEnumerable<StrongString> GetProjectionIds(
    TenantPermissionAssigned sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId) => [new(Guid.NewGuid().ToString())];
}
