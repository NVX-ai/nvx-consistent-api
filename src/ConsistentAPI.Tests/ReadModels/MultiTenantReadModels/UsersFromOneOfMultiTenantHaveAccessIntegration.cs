namespace ConsistentAPI.Tests.ReadModels.MultiTenantReadModels;

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
    await EventuallyConsistent.WaitFor(async () =>
    {
      var page = await setup.ReadModels<MultiTenantEntityReadModel>(true);
      Assert.Single(page.Items);
      var item = page.Items.Single();
      Assert.Contains(firstTenantId, item.TenantIds);
      Assert.Contains(secondTenantId, item.TenantIds);
      Assert.Equal(2, item.TenantIds.Length);
      var record = await setup.ReadModel<MultiTenantEntityReadModel>(entityId.ToString(), asAdmin: true);
      Assert.Contains(firstTenantId, record.TenantIds);
      Assert.Contains(secondTenantId, record.TenantIds);
      Assert.Equal(2, record.TenantIds.Length);
    });

    // Verify first user can read
    await EventuallyConsistent.WaitFor(async () =>
    {
      var page = await setup.ReadModels<MultiTenantEntityReadModel>(asUser: firstUserName);
      Assert.Single(page.Items);
      var item = page.Items.Single();
      Assert.Contains(firstTenantId, item.TenantIds);
      Assert.Contains(secondTenantId, item.TenantIds);
      Assert.Equal(2, item.TenantIds.Length);
      var record = await setup.ReadModel<MultiTenantEntityReadModel>(entityId.ToString(), asUser: firstUserName);
      Assert.Contains(firstTenantId, record.TenantIds);
      Assert.Contains(secondTenantId, record.TenantIds);
      Assert.Equal(2, record.TenantIds.Length);
    });

    // Verify second user cannot read
    await EventuallyConsistent.WaitFor(async () =>
    {
      var page = await setup.ReadModels<MultiTenantEntityReadModel>(asUser: secondUserName);
      Assert.Empty(page.Items);
      await setup.ReadModelNotFound<MultiTenantEntityReadModel>(entityId.ToString(), asUser: secondUserName);
    });
  }
}
