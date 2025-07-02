namespace Nvx.ConsistentAPI;

public record TemplateUserRoleId(Guid Id) : StrongId
{
  public override string StreamId() => ToString();
  public override string ToString() => Id.ToString();
}

public partial record TemplateUserRoleEntity(
  Guid TemplateUserRoleId,
  string Name,
  string Description,
  string[] Permissions,
  bool IsDeleted
) : EventModelEntity<TemplateUserRoleEntity>,
  Folds<TemplateUserRoleDescribed, TemplateUserRoleEntity>,
  Folds<TemplateUserRoleUpdated, TemplateUserRoleEntity>,
  Folds<TemplateUserRoleRemoved, TemplateUserRoleEntity>,
  Folds<TemplateUserRolePermissionAdded, TemplateUserRoleEntity>,
  Folds<TemplateUserRolePermissionRemoved, TemplateUserRoleEntity>
{
  internal const string StreamPrefix = "framework-template-user-role-";
  public string GetStreamName() => GetStreamName(TemplateUserRoleId);

  public ValueTask<TemplateUserRoleEntity> Fold(
    TemplateUserRoleDescribed evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        TemplateUserRoleId = evt.TemplateUserRoleId,
        Name = evt.Name,
        Description = evt.Description
      });

  public ValueTask<TemplateUserRoleEntity> Fold(
    TemplateUserRolePermissionAdded evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with { Permissions = Permissions.Where(r => r != evt.Permission).Append(evt.Permission).ToArray() });

  public ValueTask<TemplateUserRoleEntity> Fold(
    TemplateUserRolePermissionRemoved evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Permissions = Permissions.Where(r => r != evt.Permission).ToArray() });

  public ValueTask<TemplateUserRoleEntity> Fold(
    TemplateUserRoleRemoved evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { IsDeleted = true });

  public ValueTask<TemplateUserRoleEntity> Fold(
    TemplateUserRoleUpdated evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        Name = evt.Name,
        Description = evt.Description
      });

  public static string GetStreamName(Guid templateUserRoleId) => $"{StreamPrefix}{templateUserRoleId}";

  public static TemplateUserRoleEntity Defaulted(TemplateUserRoleId id) => new(id.Id, "", "", [], false);
}
