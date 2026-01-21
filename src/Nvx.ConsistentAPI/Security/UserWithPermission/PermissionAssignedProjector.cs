using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

internal class PermissionAssignedProjector :
  ProjectionDefinition<ApplicationPermissionAssigned, PermissionAssignedToUserProjection, UserSecurity,
    UserWithPermissionProjection, UserWithPermissionId>
{
  public override string Name => "projection-application-permission-assigned-to-denormalized";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<PermissionAssignedToUserProjection> Project(
    ApplicationPermissionAssigned eventToProject,
    UserSecurity e,
    Option<UserWithPermissionProjection> projectionEntity,
    UserWithPermissionId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata
  ) => new PermissionAssignedToUserProjection(e.Sub, e.FullName, e.Email, eventToProject.Permission);

  public override IEnumerable<UserWithPermissionId> GetProjectionIds(
    ApplicationPermissionAssigned sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId) => [new(sourceEvent.Sub, sourceEvent.Permission)];
}
