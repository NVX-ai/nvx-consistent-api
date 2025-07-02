namespace Nvx.ConsistentAPI.Tests.ReadModels.Filters;

public class FilterIsArrayEmptyIntegration
{
  [Fact(DisplayName = "filters on records that have an empty array")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var userName = Guid.NewGuid().ToString().Replace("-", "");
    _ = await setup.CurrentUser(asUser: userName);
    await EventuallyConsistent.WaitFor(async () =>
    {
      var usersBySub = await setup.ReadModels<UserSecurityReadModel>(
        true,
        queryParameters: new Dictionary<string, string[]> { { "eq-Sub", [setup.Auth.ByName(userName)] } });
      Assert.Single(usersBySub.Items);

      var users = await setup.ReadModels<UserSecurityReadModel>(
        true,
        queryParameters: new Dictionary<string, string[]>
          { { "iae-ApplicationPermissions", ["true"] }, { "eq-Sub", [setup.Auth.ByName(userName)] } });
      Assert.Single(users.Items);
    });

    var roleName = Guid.NewGuid().ToString().Replace("-", "");
    await setup.Command(new AssignApplicationPermission(setup.Auth.ByName(userName), roleName), true);
    await EventuallyConsistent.WaitFor(async () =>
    {
      var users = await setup.ReadModels<UserSecurityReadModel>(
        true,
        queryParameters: new Dictionary<string, string[]>
          { { "iae-ApplicationPermissions", ["true"] }, { "eq-Sub", [setup.Auth.ByName(userName)] } });
      Assert.Empty(users.Items);
    });
  }
}
