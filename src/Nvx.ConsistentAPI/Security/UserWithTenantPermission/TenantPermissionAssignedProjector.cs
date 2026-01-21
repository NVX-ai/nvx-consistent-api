using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

public class TenantPermissionAssignedProjector :
  ProjectionDefinition<TenantPermissionAssigned, TenantPermissionAssignedProjection, UserSecurity,
    UserWithTenantPermissionProjection, UserWithTenantPermissionId>
{
  public override string Name => "projection-tenant-permission-assigned-to-denormalized";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<TenantPermissionAssignedProjection> Project(
    TenantPermissionAssigned eventToProject,
    UserSecurity e,
    Option<UserWithTenantPermissionProjection> projectionEntity,
    UserWithTenantPermissionId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    new TenantPermissionAssignedProjection(
      e.Sub,
      eventToProject.TenantId,
      e.FullName,
      e.Email,
      eventToProject.Permission
    );

  public override IEnumerable<UserWithTenantPermissionId> GetProjectionIds(
    TenantPermissionAssigned sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId) => [new(sourceEntity.Sub, sourceEvent.TenantId, sourceEvent.Permission)];
}
