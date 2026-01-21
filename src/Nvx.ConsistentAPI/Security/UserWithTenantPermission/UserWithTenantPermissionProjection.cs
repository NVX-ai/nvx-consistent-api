namespace Nvx.ConsistentAPI;

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
