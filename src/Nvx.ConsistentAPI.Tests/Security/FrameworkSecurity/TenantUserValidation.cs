using Nvx.ConsistentAPI.TenantUsers;

namespace Nvx.ConsistentAPI.Tests.Security;

public class TenantUserValidation
{
  [Fact(DisplayName = "get tenant users as expected")]
  public async Task GetTenantUsers()
  {
    var TenantId = new StrongGuid(Guid.NewGuid());
    var userId = Guid.NewGuid();
    var entity = await TenantUsersEntity
      .Defaulted(TenantId)
      .Fold(
        new UserWasAddedToTenant(TenantId.Value, userId.ToString()),
        new EventMetadata(DateTime.UtcNow, null, null, null, null),
        null!);

    Assert.Equal(
      new TenantUserReadModel(
        TenantId.Value.ToString(),
        entity.TenantName,
        entity.Users
      ),
      TenantUserReadModel.FromEntity(entity));
  }
}
