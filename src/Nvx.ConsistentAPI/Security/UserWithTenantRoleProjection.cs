using KurrentDB.Client;

namespace Nvx.ConsistentAPI;

public record UserWithTenantPermissionId(string Sub, Guid TenantId, string Permission) : StrongId
{
  public override string StreamId() => $"{Sub}#{TenantId}#{Permission}";
  public override string ToString() => StreamId();
}

public record UserWithTenantPermissionReadModel(
  string Sub,
  Guid TenantId,
  string? Name,
  string? Email,
  string Permission,
  string Id)
  : EventModelReadModel
{
  public StrongId GetStrongId() => new UserWithTenantPermissionId(Sub, TenantId, Permission);
}

public partial record UserWithTenantPermissionProjection(
  string Sub,
  Guid TenantId,
  string Name,
  string Email,
  string Permission,
  bool IsActive)
  : EventModelEntity<UserWithTenantPermissionProjection>,
    Folds<TenantPermissionAssignedProjection, UserWithTenantPermissionProjection>,
    Folds<TenantPermissionRevokedProjection, UserWithTenantPermissionProjection>,
    Folds<TenantPermissionNameReceivedProjection, UserWithTenantPermissionProjection>,
    Folds<TenantPermissionEmailReceivedProjection, UserWithTenantPermissionProjection>
{
  public const string StreamPrefix = "framework-user-with-tenant-permission-";

  public static readonly EventModel Get =
    new()
    {
      ReadModels =
      [
        new ReadModelDefinition<UserWithTenantPermissionReadModel, UserWithTenantPermissionProjection>
        {
          StreamPrefix = StreamPrefix,
          Projector = entity =>
            entity.IsActive
              ?
              [
                new UserWithTenantPermissionReadModel(
                  entity.Sub,
                  entity.TenantId,
                  entity.Name,
                  entity.Email,
                  entity.Permission,
                  $"{entity.Sub}#{entity.TenantId}#{entity.Permission}"
                )
              ]
              : [],
          Auth = new PermissionsRequireOne("admin"),
          AreaTag = OperationTags.Authorization
        }
      ],
      Entities =
      [
        new EntityDefinition<UserWithTenantPermissionProjection, UserWithTenantPermissionId>
        {
          Defaulter = Defaulted, StreamPrefix = StreamPrefix
        }
      ],
      Projections =
      [
        new TenantPermissionAssignedProjector(),
        new TenantPermissionNameReceivedProjector(),
        new TenantPermissionEmailReceivedProjector(),
        new TenantPermissionRevokedProjector()
      ]
    };

  private string EntityId => $"{Sub}#{TenantId}#{Permission}";

  public string GetStreamName() => $"{StreamPrefix}{EntityId}";

  public ValueTask<UserWithTenantPermissionProjection> Fold(
    TenantPermissionAssignedProjection tra,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        Name = tra.Name,
        TenantId = tra.TenantId,
        Email = tra.Email,
        Permission = tra.Permission,
        IsActive = true
      });

  public ValueTask<UserWithTenantPermissionProjection> Fold(
    TenantPermissionEmailReceivedProjection evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Email = evt.Email });

  public ValueTask<UserWithTenantPermissionProjection> Fold(
    TenantPermissionNameReceivedProjection evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Name = evt.Name });

  public ValueTask<UserWithTenantPermissionProjection> Fold(
    TenantPermissionRevokedProjection _,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { IsActive = false });

  public static UserWithTenantPermissionProjection Defaulted(UserWithTenantPermissionId id) =>
    new(id.Sub, id.TenantId, string.Empty, string.Empty, id.Permission, false);
}

public record TenantPermissionAssignedProjection(
  string Sub,
  Guid TenantId,
  string Name,
  string Email,
  string Permission)
  : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithTenantPermissionId(Sub, TenantId, Permission);
  public string GetStreamName() => $"{UserWithTenantPermissionProjection.StreamPrefix}{GetEntityId()}";
}

public record TenantPermissionRevokedProjection(string Sub, Guid TenantId, string Permission)
  : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithTenantPermissionId(Sub, TenantId, Permission);
  public string GetStreamName() => $"{UserWithTenantPermissionProjection.StreamPrefix}{GetEntityId()}";
}

public record TenantPermissionNameReceivedProjection(string Sub, Guid TenantId, string Permission, string Name)
  : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithTenantPermissionId(Sub, TenantId, Permission);
  public string GetStreamName() => $"{UserWithTenantPermissionProjection.StreamPrefix}{GetEntityId()}";
}

public record TenantPermissionEmailReceivedProjection(string Sub, Guid TenantId, string Permission, string Email)
  : EventModelEvent
{
  public StrongId GetEntityId() => new UserWithTenantPermissionId(Sub, TenantId, Permission);
  public string GetStreamName() => $"{UserWithTenantPermissionProjection.StreamPrefix}{GetEntityId()}";
}

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
