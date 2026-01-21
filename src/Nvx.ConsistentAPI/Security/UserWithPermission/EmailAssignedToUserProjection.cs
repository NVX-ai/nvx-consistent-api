using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

internal class EmailAssignedToUserProjection :
  ProjectionDefinition<
    UserEmailReceived,
    EmailReceivedForUserProjection,
    UserSecurity,
    UserWithPermissionProjection, UserWithPermissionId>
{
  public override string Name => "projection-user-email-received-to-denormalized";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<EmailReceivedForUserProjection> Project(
    UserEmailReceived uer,
    UserSecurity e,
    Option<UserWithPermissionProjection> projectionEntity,
    UserWithPermissionId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata
  ) => projectionEntity.Map(uwr => new EmailReceivedForUserProjection(uwr.Sub, uwr.Permission, uer.Email));

  public override IEnumerable<UserWithPermissionId> GetProjectionIds(
    UserEmailReceived sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId
  ) => sourceEntity
    .ApplicationPermissions.Select(permission => new UserWithPermissionId(sourceEntity.Sub, permission))
    .ToArray();
}
