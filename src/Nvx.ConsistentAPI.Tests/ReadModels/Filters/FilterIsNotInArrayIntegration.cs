namespace Nvx.ConsistentAPI.Tests.ReadModels.Filters;

public class FilterIsNotInArrayIntegration
{
  [Fact(DisplayName = "filters on records that do not have a value in the array")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var userName = Guid.NewGuid().ToString().Replace("-", "");
    var roleName = Guid.NewGuid().ToString().Replace("-", "");
    _ = await setup.CurrentUser(asUser: userName);
    await setup.Command(new AssignApplicationPermission(setup.Auth.ByName(userName), roleName), true);

    var usersBySub = await setup.ReadModels<UserSecurityReadModel>(
      true,
      queryParameters: new Dictionary<string, string[]> { { "eq-Sub", [setup.Auth.ByName(userName)] } });
    Assert.Single(usersBySub.Items);

    var users = await setup.ReadModels<UserSecurityReadModel>(
      true,
      queryParameters: new Dictionary<string, string[]>
        { { "nia-ApplicationPermissions", [roleName] }, { "eq-Sub", [setup.Auth.ByName(userName)] } });
    Assert.Empty(users.Items);
  }
}
