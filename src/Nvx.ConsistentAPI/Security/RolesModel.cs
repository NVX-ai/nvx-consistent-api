namespace Nvx.ConsistentAPI;

public record RoleId(Guid Id, Guid TenantId) : StrongId
{
  public override string StreamId() => ToString();
  public override string ToString() => $"{Id}-{TenantId}";
}

public static class RolesModel
{
  public static readonly EventModel Get = new()
  {
    Entities =
    [
      new EntityDefinition<RoleEntity, RoleId>
      {
        Defaulter = RoleEntity.Defaulted,
        StreamPrefix = RoleEntity.StreamPrefix
      }
    ],
    ReadModels =
    [
      new ReadModelDefinition<RoleReadModel, RoleEntity>
      {
        StreamPrefix = RoleEntity.StreamPrefix,
        Projector = role =>
        [
          new RoleReadModel(
            new RoleId(role.Id, role.TenantId).ToString(),
            role.Id,
            role.Name,
            role.Description,
            role.Permissions,
            role.TenantId)
        ],
        AreaTag = OperationTags.Authorization
      }
    ],
    Commands =
    [
      new CommandDefinition<AddPermissionToRole, RoleEntity>
      {
        Auth = new PermissionsRequireOne("admin"),
        AreaTag = OperationTags.Authorization
      },
      new CommandDefinition<RemovePermissionFromRole, RoleEntity>
      {
        Auth = new PermissionsRequireOne("admin"),
        AreaTag = OperationTags.Authorization
      },
      new CommandDefinition<CreateRole, RoleEntity>
      {
        Auth = new PermissionsRequireOne("admin"),
        AreaTag = OperationTags.Authorization
      },
      new CommandDefinition<CreateRoleFromTemplate, RoleEntity>
      {
        Auth = new PermissionsRequireOne("admin"),
        AreaTag = OperationTags.Authorization
      }
    ]
  };
}

public record CreateRoleFromTemplate(Guid TemplateId) : TenantEventModelCommand<RoleEntity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new RoleId(TemplateId, tenantId);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<RoleEntity> entity,
    UserSecurity user,
    FileUpload[] files) =>
    this.ShouldCreate(entity, () => new RoleCreatedFromTemplate(Guid.NewGuid(), TemplateId, tenantId).ToEventArray());
}

public record CreateRole(string Name, string Description) : TenantEventModelCommand<RoleEntity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new RoleId(Guid.NewGuid(), tenantId);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<RoleEntity> entity,
    UserSecurity user,
    FileUpload[] files) =>
    this.ShouldCreate(entity, () => new RoleCreated(Guid.NewGuid(), Name, Description, tenantId).ToEventArray());
}

public record AddPermissionToRole(Guid Id, string Permission) : TenantEventModelCommand<RoleEntity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new RoleId(Id, tenantId);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<RoleEntity> entity,
    UserSecurity user,
    FileUpload[] files) => this.Require(
    entity,
    user,
    tenantId,
    _ => new ExistingStream(new PermissionAddedToRole(Id, Permission, tenantId)));
}

public record RemovePermissionFromRole(Guid Id, string Permission) : TenantEventModelCommand<RoleEntity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new RoleId(Id, tenantId);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<RoleEntity> entity,
    UserSecurity user,
    FileUpload[] files) => this.Require(
    entity,
    user,
    tenantId,
    _ => new ExistingStream(new PermissionRemovedFromRole(Id, Permission, tenantId)));
}

public record RoleCreatedFromTemplate(Guid Id, Guid TemplateId, Guid TenantId) : EventModelEvent
{
  public string GetSwimlane() => RoleEntity.StreamPrefix;
  public StrongId GetEntityId() => new RoleId(Id, TenantId);
}

public record RoleCreated(Guid Id, string Name, string Description, Guid TenantId) : EventModelEvent
{
  public string GetSwimlane() => RoleEntity.StreamPrefix;
  public StrongId GetEntityId() => new RoleId(Id, TenantId);
}

public record PermissionAddedToRole(Guid Id, string Permission, Guid TenantId) : EventModelEvent
{
  public string GetSwimlane() => RoleEntity.StreamPrefix;
  public StrongId GetEntityId() => new RoleId(Id, TenantId);
}

public record PermissionRemovedFromRole(Guid Id, string Permission, Guid TenantId) : EventModelEvent
{
  public string GetSwimlane() => RoleEntity.StreamPrefix;
  public StrongId GetEntityId() => new RoleId(Id, TenantId);
}

public partial record RoleEntity(Guid Id, string Name, string Description, string[] Permissions, Guid TenantId)
  : EventModelEntity<RoleEntity>,
    Folds<PermissionAddedToRole, RoleEntity>,
    Folds<PermissionRemovedFromRole, RoleEntity>,
    Folds<RoleCreatedFromTemplate, RoleEntity>,
    Folds<RoleCreated, RoleEntity>
{
  public const string StreamPrefix = "framework-role-entity-";

  public string GetStreamName() => GetStreamName(new RoleId(Id, TenantId));

  public ValueTask<RoleEntity> Fold(
    PermissionAddedToRole evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        Permissions = Permissions.Append(evt.Permission).Distinct().ToArray()
      });

  public ValueTask<RoleEntity> Fold(
    PermissionRemovedFromRole evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with { Permissions = [.. Permissions.Where(p => p != evt.Permission)] });

  public ValueTask<RoleEntity> Fold(RoleCreated evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        Name = evt.Name,
        Description = evt.Description,
        Permissions = []
      });

  public async ValueTask<RoleEntity> Fold(
    RoleCreatedFromTemplate evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await fetcher
      .Fetch<TemplateUserRoleEntity>(new TemplateUserRoleId(evt.TemplateId))
      .Match(
        t => this with
        {
          Description = t.Description,
          Name = t.Name,
          Permissions = t.Permissions
        },
        () => this with
        {
          Description = $"Template with id {evt.TemplateId} not found",
          Name = "unknown",
          Permissions = []
        }
      );

  public static string GetStreamName(RoleId id) => $"{StreamPrefix}{id}";

  public static RoleEntity Defaulted(RoleId id) =>
    new(id.Id, string.Empty, string.Empty, [], id.TenantId);
}

public record RoleReadModel(
  string Id,
  Guid RoleId,
  string Name,
  string Description,
  string[] Permissions,
  Guid TenantId)
  : EventModelReadModel, IsTenantBound
{
  public StrongId GetStrongId() => new RoleId(RoleId, TenantId);
}
