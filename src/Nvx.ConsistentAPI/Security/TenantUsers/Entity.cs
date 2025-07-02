namespace Nvx.ConsistentAPI.TenantUsers;

public partial record TenantUsersEntity(Guid TenantId, string TenantName, string[] Users) :
  EventModelEntity<TenantUsersEntity>,
  Folds<UserWasAddedToTenant, TenantUsersEntity>,
  Folds<UserWasRemovedFromTenant, TenantUsersEntity>,
  Folds<TenantNameWasChanged, TenantUsersEntity>
{
  public const string StreamPrefix = "tenant-users-entity-";
  public string GetStreamName() => GetStreamName(TenantId);

  public ValueTask<TenantUsersEntity> Fold(
    TenantNameWasChanged evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { TenantName = evt.NewName });

  public ValueTask<TenantUsersEntity> Fold(
    UserWasAddedToTenant evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Users = Users.Append(evt.UserId).Distinct().ToArray() });

  public ValueTask<TenantUsersEntity> Fold(
    UserWasRemovedFromTenant evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Users = Users.Where(u => u != evt.UserId).ToArray() });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";
  public static TenantUsersEntity Defaulted(StrongGuid id) => new(id.Value, string.Empty, []);
}
