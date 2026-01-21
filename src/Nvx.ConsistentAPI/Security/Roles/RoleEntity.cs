namespace Nvx.ConsistentAPI;

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
