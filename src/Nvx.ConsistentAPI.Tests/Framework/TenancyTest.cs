namespace Nvx.ConsistentAPI.Tests.Framework;

public class TenancyTest
{
  [Fact(DisplayName = "handles tenancy access")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();

    var tenant1Id = Guid.NewGuid();
    var tenant2Id = Guid.NewGuid();
    var tenant3Id = Guid.NewGuid();
    var tenant1Name = Guid.NewGuid().ToString();
    var tenant2Name = Guid.NewGuid().ToString();
    await setup.Command(new CreateTenant(tenant1Id, tenant1Name), true);
    await setup.Command(new CreateTenant(tenant2Id, tenant2Name), true);
    await setup.Command(new AddToTenant(setup.Auth.CandoSub), true, tenant1Id);
    await setup.Command(new AddToTenant(setup.Auth.CandoSub), true, tenant2Id);
    await setup.Command(new AssignTenantPermission(setup.Auth.CandoSub, "banana"), true, tenant3Id);
    var building1Name = Guid.NewGuid().ToString();
    var building2Name = Guid.NewGuid().ToString();
    var building3Name = Guid.NewGuid().ToString();
    await setup.Command(new RegisterOrganizationBuilding(building1Name), true, tenant1Id);
    await setup.Command(new RegisterOrganizationBuilding(building2Name), true, tenant1Id);
    await setup.Command(new RegisterOrganizationBuilding(building3Name), true, tenant2Id);

    var buildings1 = await setup.ReadModels<OrganizationBuildingReadModel>(tenantId: tenant1Id);
    Assert.Equal(2, buildings1.Total);
    Assert.Contains(buildings1.Items, model => model.Name == building1Name);
    Assert.Contains(buildings1.Items, model => model.Name == building2Name);
    var buildings2 = await setup.ReadModels<OrganizationBuildingReadModel>(tenantId: tenant2Id);
    Assert.Single(buildings2.Items);
    Assert.Equal(building3Name, buildings2.Items.ElementAt(0).Name);
    var currentUser = await setup.CurrentUser();
    Assert.Contains(currentUser.Tenants, td => td.TenantId == tenant1Id && td.TenantName == tenant1Name);
    Assert.Contains(currentUser.Tenants, td => td.TenantId == tenant2Id && td.TenantName == tenant2Name);
    Assert.Contains(currentUser.Tenants, td => td.TenantId == tenant3Id && td.TenantName == "");
    Assert.Equal("banana", currentUser.TenantPermissions[tenant3Id].First());

    var newTenant1Name = Guid.NewGuid().ToString();
    var newTenant3Name = Guid.NewGuid().ToString();
    var candoBeforeRename = await setup.CurrentUser();
    Assert.Contains(candoBeforeRename.Tenants, td => td.TenantId == tenant1Id && td.TenantName == tenant1Name);
    Assert.Contains(candoBeforeRename.Tenants, td => td.TenantId == tenant2Id && td.TenantName == tenant2Name);
    await setup.Command(new RenameTenant(tenant1Id, newTenant1Name), true);
    await setup.Command(new RenameTenant(tenant3Id, newTenant3Name), true);

    var canDoAfterRename = await setup.CurrentUser();
    Assert.Contains(canDoAfterRename.Tenants, td => td.TenantId == tenant1Id && td.TenantName == newTenant1Name);
    Assert.Contains(canDoAfterRename.Tenants, td => td.TenantId == tenant2Id && td.TenantName == tenant2Name);
    Assert.Contains(canDoAfterRename.Tenants, td => td.TenantId == tenant3Id && td.TenantName == newTenant3Name);

    await setup.Command(new RemoveFromTenant(setup.Auth.CandoSub), true, tenant3Id);

    var canDoUserAfter = await setup.CurrentUser();
    Assert.DoesNotContain(canDoUserAfter.Tenants, td => td.TenantId == tenant3Id);
    Assert.Contains(canDoUserAfter.Tenants, td => td.TenantId == tenant1Id && td.TenantName == newTenant1Name);
    Assert.Contains(canDoUserAfter.Tenants, td => td.TenantId == tenant2Id && td.TenantName == tenant2Name);
  }
}
