using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

internal class NameAssignedToUserProjection :
  ProjectionDefinition<
    UserNameReceived,
    NameReceivedForUserProjection,
    UserSecurity,
    UserWithPermissionProjection, UserWithPermissionId>
{
  public override string Name => "projection-user-name-received-to-denormalized";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<NameReceivedForUserProjection> Project(
    UserNameReceived unr,
    UserSecurity e,
    Option<UserWithPermissionProjection> projectionEntity,
    UserWithPermissionId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata
  ) => projectionEntity.Map(uwr => new NameReceivedForUserProjection(uwr.Sub, uwr.Permission, unr.FullName));

  public override IEnumerable<UserWithPermissionId> GetProjectionIds(
    UserNameReceived sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId
  ) => sourceEntity
    .ApplicationPermissions.Select(permission => new UserWithPermissionId(sourceEntity.Sub, permission))
    .ToArray();
}
