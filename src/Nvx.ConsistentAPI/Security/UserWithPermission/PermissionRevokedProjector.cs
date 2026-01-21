using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

internal class PermissionRevokedProjector :
  ProjectionDefinition<
    ApplicationPermissionRevoked,
    PermissionRemovedFromUserProjection,
    UserSecurity,
    UserWithPermissionProjection, UserWithPermissionId>
{
  public override string Name => "projection-application-permission-revoked-to-denormalized";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<PermissionRemovedFromUserProjection> Project(
    ApplicationPermissionRevoked eventToProject,
    UserSecurity e,
    Option<UserWithPermissionProjection> projectionEntity,
    UserWithPermissionId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata
  ) => new PermissionRemovedFromUserProjection(e.Sub, eventToProject.Permission);

  public override IEnumerable<UserWithPermissionId> GetProjectionIds(
    ApplicationPermissionRevoked sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId
  ) => [new(sourceEvent.Sub, sourceEvent.Permission)];
}
