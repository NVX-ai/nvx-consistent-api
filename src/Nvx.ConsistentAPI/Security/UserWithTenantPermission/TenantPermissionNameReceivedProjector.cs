using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

public class TenantPermissionNameReceivedProjector :
  ProjectionDefinition<UserNameReceived, TenantPermissionNameReceivedProjection, UserSecurity,
    UserWithTenantPermissionProjection, UserWithTenantPermissionId>
{
  public override string Name => "projection-tenant-permission-name-received-to-denormalized";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<TenantPermissionNameReceivedProjection> Project(
    UserNameReceived evt,
    UserSecurity e,
    Option<UserWithTenantPermissionProjection> projectionEntity,
    UserWithTenantPermissionId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    projectionEntity
      .Map(uwtr => new TenantPermissionNameReceivedProjection(uwtr.Sub, uwtr.TenantId, uwtr.Permission, evt.FullName));

  public override IEnumerable<UserWithTenantPermissionId> GetProjectionIds(
    UserNameReceived sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId
  ) =>
    sourceEntity
      .TenantPermissions
      .SelectMany(tenant => tenant.Value
        .Select(p => new UserWithTenantPermissionId(sourceEntity.Sub, tenant.Key, p)))
      .ToArray();
}
