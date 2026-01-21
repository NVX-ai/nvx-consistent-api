namespace Nvx.ConsistentAPI;

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
