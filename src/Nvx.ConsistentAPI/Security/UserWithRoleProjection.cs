using EventStore.Client;

namespace Nvx.ConsistentAPI;

public record UserWithPermissionId(string Sub, string Permission) : StrongId
{
  public override string StreamId() => $"{Sub}#{Permission}";
  public override string ToString() => StreamId();
}

public record UserWithPermissionReadModel(string Sub, string? Name, string? Email, string Permission, string Id)
  : EventModelReadModel
{
  public StrongId GetStrongId() => new UserWithPermissionId(Sub, Permission);
}

public partial record UserWithPermissionProjection(
  string Sub,
  string Name,
  string Email,
  string Permission,
  bool IsActive)
  : EventModelEntity<UserWithPermissionProjection>,
    Folds<PermissionAssignedToUserProjection, UserWithPermissionProjection>,
    Folds<PermissionRemovedFromUserProjection, UserWithPermissionProjection>,
    Folds<NameReceivedForUserProjection, UserWithPermissionProjection>,
    Folds<EmailReceivedForUserProjection, UserWithPermissionProjection>
{
  public const string StreamPrefix = "framework-user-with-permission-";

  public static readonly EventModel Get =
    new()
    {
      Entities =
      [
        new EntityDefinition<UserWithPermissionProjection, UserWithPermissionId>
        {
          Defaulter = Defaulted, StreamPrefix = StreamPrefix
        }
      ],
      ReadModels =
      [
        new ReadModelDefinition<UserWithPermissionReadModel, UserWithPermissionProjection>
        {
          StreamPrefix = StreamPrefix,
          Projector = entity =>
            entity.IsActive
              ?
              [
                new UserWithPermissionReadModel(
                  entity.Sub,
                  entity.Name,
                  entity.Email,
                  entity.Permission,
                  $"{entity.Sub}#{entity.Permission}"
                )
              ]
              : [],
          Auth = new PermissionsRequireOne("admin"),
          AreaTag = OperationTags.Authorization
        }
      ],
      Projections =
      [
        new PermissionAssignedProjector(),
        new PermissionRevokedProjector(),
        new NameAssignedToUserProjection(),
        new EmailAssignedToUserProjection()
      ]
    };

  private string EntityId => $"{Sub}#{Permission}";

  public string GetStreamName() => $"{StreamPrefix}{EntityId}";

  public ValueTask<UserWithPermissionProjection> Fold(
    EmailReceivedForUserProjection er,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Email = er.Email });

  public ValueTask<UserWithPermissionProjection> Fold(
    NameReceivedForUserProjection nr,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Name = nr.Name });

  public ValueTask<UserWithPermissionProjection> Fold(
    PermissionAssignedToUserProjection ra,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Name = ra.Name, Email = ra.Email, Permission = ra.Permission, IsActive = true });

  public ValueTask<UserWithPermissionProjection> Fold(
    PermissionRemovedFromUserProjection _,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { IsActive = false });

  public static UserWithPermissionProjection Defaulted(UserWithPermissionId id) =>
    new(id.Sub, string.Empty, string.Empty, id.Permission, false);
}

public record PermissionAssignedToUserProjection(string Sub, string Name, string Email, string Permission)
  : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithPermissionId(Sub, Permission);
  public string GetSwimLane() => UserWithPermissionProjection.StreamPrefix;
}

public record PermissionRemovedFromUserProjection(string Sub, string Permission) : EventModelEvent
{
  public string GetSwimLane() => UserWithPermissionProjection.StreamPrefix;
  public StrongId GetEntityId() => new UserWithPermissionId(Sub, Permission);
}

public record NameReceivedForUserProjection(string Sub, string Permission, string Name) : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithPermissionId(Sub, Permission);
  public string GetSwimLane() => UserWithPermissionProjection.StreamPrefix;
}

public record EmailReceivedForUserProjection(string Sub, string Permission, string Email) : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithPermissionId(Sub, Permission);
  public string GetSwimLane() => UserWithPermissionProjection.StreamPrefix;
}

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
    Uuid sourceEventId) =>
    [new(sourceEvent.Sub, sourceEvent.Permission)];
}

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
