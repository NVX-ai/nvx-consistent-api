namespace Nvx.ConsistentAPI.Tests.ReadModels.MultiTenantReadModels;

public class UsersFromOneOfMultiTenantHaveAccessIntegration
{
  [Fact(
    DisplayName = "only users with the appropriate tenant permission and admins can read a multi tenant read model")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var entityId = Guid.NewGuid();
    var firstTenantId = Guid.NewGuid();
    var secondTenantId = Guid.NewGuid();
    var firstUserName = Guid.NewGuid().ToString();
    var firstUserSub = setup.Auth.ByName(firstUserName);
    var secondUserName = Guid.NewGuid().ToString();

    // Setup permissions
    await setup.Command(
      new AssignTenantPermission(firstUserSub, MultiTenantModel.Permission),
      tenantId: firstTenantId,
      asAdmin: true);
    await setup.Command(
      new AssignTenantPermission(firstUserSub, MultiTenantModel.Permission),
      tenantId: secondTenantId,
      asAdmin: true);

    // Setup entity
    await setup.InsertEvents(
      new MultiTenantEntityReceivedTenant(entityId, firstTenantId),
      new MultiTenantEntityReceivedTenant(entityId, secondTenantId));

    // Verify admin can read
    var adminPage = await setup.ReadModels<MultiTenantEntityReadModel>(true);
    Assert.Single(adminPage.Items);
    var adminItem = adminPage.Items.Single();
    Assert.Contains(firstTenantId, adminItem.TenantIds);
    Assert.Contains(secondTenantId, adminItem.TenantIds);
    Assert.Equal(2, adminItem.TenantIds.Length);
    var adminRecord = await setup.ReadModel<MultiTenantEntityReadModel>(entityId.ToString(), asAdmin: true);
    Assert.Contains(firstTenantId, adminRecord.TenantIds);
    Assert.Contains(secondTenantId, adminRecord.TenantIds);
    Assert.Equal(2, adminRecord.TenantIds.Length);

    // Verify first user can read
    var firstUserPage = await setup.ReadModels<MultiTenantEntityReadModel>(asUser: firstUserName);
    Assert.Single(firstUserPage.Items);
    var firstUserItem = firstUserPage.Items.Single();
    Assert.Contains(firstTenantId, firstUserItem.TenantIds);
    Assert.Contains(secondTenantId, firstUserItem.TenantIds);
    Assert.Equal(2, firstUserItem.TenantIds.Length);
    var firstUserRecord = await setup.ReadModel<MultiTenantEntityReadModel>(entityId.ToString(), asUser: firstUserName);
    Assert.Contains(firstTenantId, firstUserRecord.TenantIds);
    Assert.Contains(secondTenantId, firstUserRecord.TenantIds);
    Assert.Equal(2, firstUserRecord.TenantIds.Length);

    // Verify second user cannot read
    var secondUserPage = await setup.ReadModels<MultiTenantEntityReadModel>(asUser: secondUserName);
    Assert.Empty(secondUserPage.Items);
    await setup.ReadModelNotFound<MultiTenantEntityReadModel>(entityId.ToString(), asUser: secondUserName);
  }
}
