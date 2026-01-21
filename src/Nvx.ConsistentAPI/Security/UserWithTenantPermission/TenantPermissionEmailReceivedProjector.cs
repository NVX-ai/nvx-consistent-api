using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

public class TenantPermissionEmailReceivedProjector :
  ProjectionDefinition<
    UserEmailReceived,
    TenantPermissionEmailReceivedProjection,
    UserSecurity,
    UserWithTenantPermissionProjection, UserWithTenantPermissionId>
{
  public override string Name => "projection-tenant-permission-email-received-to-denormalized";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<TenantPermissionEmailReceivedProjection> Project(
    UserEmailReceived evt,
    UserSecurity e,
    Option<UserWithTenantPermissionProjection> projectionEntity,
    UserWithTenantPermissionId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    projectionEntity
      .Map(uwtr => new TenantPermissionEmailReceivedProjection(uwtr.Sub, uwtr.TenantId, uwtr.Permission, evt.Email));

  public override IEnumerable<UserWithTenantPermissionId> GetProjectionIds(
    UserEmailReceived sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId
  ) =>
    sourceEntity
      .TenantPermissions.SelectMany(tenant =>
        tenant.Value.Select(permission => new UserWithTenantPermissionId(sourceEntity.Sub, tenant.Key, permission))
      )
      .ToArray();
}
