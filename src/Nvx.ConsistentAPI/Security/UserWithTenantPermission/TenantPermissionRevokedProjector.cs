using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

public class TenantPermissionRevokedProjector :
  ProjectionDefinition<
    TenantPermissionRevoked,
    TenantPermissionRevokedProjection,
    UserSecurity,
    UserWithTenantPermissionProjection, UserWithTenantPermissionId>
{
  public override string Name => "projection-tenant-permission-revoked-to-denormalized";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<TenantPermissionRevokedProjection> Project(
    TenantPermissionRevoked eventToProject,
    UserSecurity e,
    Option<UserWithTenantPermissionProjection> projectionEntity,
    UserWithTenantPermissionId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    new TenantPermissionRevokedProjection(
      e.Sub,
      eventToProject.TenantId,
      eventToProject.Permission
    );

  public override IEnumerable<UserWithTenantPermissionId> GetProjectionIds(
    TenantPermissionRevoked sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId) => [new(sourceEntity.Sub, sourceEvent.TenantId, sourceEvent.Permission)];
}
