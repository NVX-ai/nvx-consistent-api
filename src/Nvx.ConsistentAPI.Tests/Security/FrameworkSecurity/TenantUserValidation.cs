using Nvx.ConsistentAPI.TenantUsers;

namespace Nvx.ConsistentAPI.Tests.Security;

public class TenantUserValidation
{
  [Fact(DisplayName = "get tenant users as expected")]
  public async Task GetTenantUsers()
  {
    var tenantId = new StrongGuid(Guid.NewGuid());
    var userId = Guid.NewGuid();
    var entity = await TenantUsersEntity
      .Defaulted(tenantId)
      .Fold(
        new UserWasAddedToTenant(tenantId.Value, userId.ToString()),
        new EventMetadata(DateTime.UtcNow, null, null, null, null, null),
        null!);

    Assert.Equal(
      new TenantUserReadModel(
        tenantId.Value.ToString(),
        entity.TenantName,
        entity.Users
      ),
      TenantUserReadModel.FromEntity(entity));
  }
}
