namespace Nvx.ConsistentAPI.Tests.ReadModels.Filters;

public class FilterIsArrayEmptyIntegration
{
  [Fact(DisplayName = "filters on records that have an empty array")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var userName = Guid.NewGuid().ToString().Replace("-", "");

    var permissionName = Guid.NewGuid().ToString().Replace("-", "");

    await setup.Command(new AssignApplicationPermission(setup.Auth.ByName(userName), permissionName), true);
    var usersWithPermission = await setup.ReadModels<UserSecurityReadModel>(
      true,
      queryParameters: new Dictionary<string, string[]>
        { { "iae-ApplicationPermissions", ["true"] }, { "eq-Sub", [setup.Auth.ByName(userName)] } });
    Assert.Empty(usersWithPermission.Items);

    await setup.Command(new RevokeApplicationPermission(setup.Auth.ByName(userName), permissionName), true);
    var usersWithoutPermissions = await setup.ReadModels<UserSecurityReadModel>(
      true,
      queryParameters: new Dictionary<string, string[]>
        { { "iae-ApplicationPermissions", ["true"] }, { "eq-Sub", [setup.Auth.ByName(userName)] } });
    Assert.Single(usersWithoutPermissions.Items);
  }
}
