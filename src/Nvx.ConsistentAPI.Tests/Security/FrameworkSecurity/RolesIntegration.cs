using System.Net;

namespace Nvx.ConsistentAPI.Tests.Security;

public class RolesIntegration
{
  [Fact(DisplayName = "assigning a role to a user applies the permissions of that role at tenant level")]
  public async Task AssignTenant()
  {
    await using var setup = await Initializer.Do();
    var (roleId, _, tenantId, userName, userSub) = await CreateRole(setup);
    await setup.Command(new AssignTenantRole(userSub, roleId), true, tenantId);
    await setup.WaitForConsistency();
    await setup.Command(new ActUponPermissionsAndRolesEntity(Guid.NewGuid()), tenantId: tenantId, asUser: userName);
  }

  [Fact(DisplayName = "after assigning a tenant permission revoking a role does not remove it")]
  public async Task AssignTenantPermission()
  {
    await using var setup = await Initializer.Do();
    var (roleId, _, tenantId, userName, userSub) = await CreateRole(setup);
    await setup.Command(
      new AssignTenantPermission(userSub, ActUponPermissionsAndRolesEntity.Permission),
      true,
      tenantId);
    await setup.WaitForConsistency();
    await setup.Command(new ActUponPermissionsAndRolesEntity(Guid.NewGuid()), tenantId: tenantId, asUser: userName);
    await setup.Command(new RevokeTenantRole(userSub, roleId), tenantId: tenantId, asAdmin: true);
    await setup.WaitForConsistency();
    await setup.Command(new ActUponPermissionsAndRolesEntity(Guid.NewGuid()), tenantId: tenantId, asUser: userName);
  }

  [Fact(DisplayName = "after assigning a tenant role the permissions show in the read model")]
  public async Task AssignTenantRole()
  {
    await using var setup = await Initializer.Do();
    var (roleId, _, tenantId, _, userSub) = await CreateRole(setup);
    await setup.Command(new AssignTenantRole(userSub, roleId), true, tenantId);
    var user = await setup.ReadModel<UserSecurityReadModel>(
      userSub,
      asAdmin: true,
      waitType: ConsistencyWaitType.Tasks);
    Assert.Contains(user.TenantPermissions[tenantId], p => p == ActUponPermissionsAndRolesEntity.Permission);
  }

  [Fact(DisplayName = "after assigning a tenant role and then adding a permission to the role shows in the read model")]
  public async Task AssignTenantRoleAddPermission()
  {
    await using var setup = await Initializer.Do();
    var (roleId, _, tenantId, _, userSub) = await CreateRole(setup);
    var newPermission = Guid.NewGuid().ToString();
    var anotherPermission = Guid.NewGuid().ToString();
    await setup.Command(new AssignTenantRole(userSub, roleId), true, tenantId);
    await setup.Command(new AddPermissionToRole(roleId, newPermission), true, tenantId);
    await setup.Command(new AddPermissionToRole(roleId, anotherPermission), true, tenantId);
    var user = await setup.ReadModel<UserSecurityReadModel>(
      userSub,
      asAdmin: true,
      waitType: ConsistencyWaitType.Tasks);
    Assert.Contains(user.TenantPermissions[tenantId], p => p == ActUponPermissionsAndRolesEntity.Permission);
    Assert.Contains(user.TenantPermissions[tenantId], p => p == newPermission);
    Assert.Contains(user.TenantPermissions[tenantId], p => p == anotherPermission);
  }

  private static async Task<(Guid roleId, string roleName, Guid tenantId, string userName, string userSub)>
    CreateRole(TestSetup setup)
  {
    var tenantId = Guid.NewGuid();
    var userName = Guid.NewGuid().ToString();
    var userSub = setup.Auth.ByName(userName);
    var roleName = Guid.NewGuid().ToString();
    var roleDescription = Guid.NewGuid().ToString();
    await setup.FailingCommand(
      new ActUponPermissionsAndRolesEntity(Guid.NewGuid()),
      (int)HttpStatusCode.Forbidden,
      asUser: userName,
      tenantId: tenantId);
    var roleId = await setup
      .Command(new CreateRole(roleName, roleDescription), true, tenantId)
      .Map(car => Guid.Parse(car.EntityId[..36]));
    await setup.Command(new AddPermissionToRole(roleId, ActUponPermissionsAndRolesEntity.Permission), true, tenantId);
    await setup.Command(new AssignTenantRole(userSub, roleId), true, tenantId);
    return (roleId, roleName, tenantId, userName, userSub);
  }
}
