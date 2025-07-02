namespace Nvx.ConsistentAPI.Tests.ReadModels.Filters;

public class FilterIsInArrayIntegration
{
  [Fact(DisplayName = "filters on records that have a value in the array")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var userName = Guid.NewGuid().ToString().Replace("-", "");
    var roleName = Guid.NewGuid().ToString().Replace("-", "");
    _ = await setup.CurrentUser(asUser: userName);
    await setup.Command(new AssignApplicationPermission(setup.Auth.ByName(userName), roleName), true);
    await EventuallyConsistent.WaitFor(async () =>
    {
      var users = await setup.ReadModels<UserSecurityReadModel>(
        true,
        queryParameters: new Dictionary<string, string[]> { { "ia-ApplicationPermissions", [roleName] } });
      Assert.Single(users.Items);
    });
    var usersWithNoFilters = await setup.ReadModels<UserSecurityReadModel>(true);
    Assert.True(usersWithNoFilters.Items.Count() > 1);
  }
}
