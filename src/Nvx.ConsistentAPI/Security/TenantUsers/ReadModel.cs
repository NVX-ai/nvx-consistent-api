namespace Nvx.ConsistentAPI.TenantUsers;

public record TenantUserReadModel(string Id, string TenantName, string[] Users) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongGuid(Guid.Parse(Id));

  public static Option<TenantUserReadModel> FromEntity(TenantUsersEntity entity) =>
    new TenantUserReadModel(entity.TenantId.ToString(), entity.TenantName, entity.Users);
}
