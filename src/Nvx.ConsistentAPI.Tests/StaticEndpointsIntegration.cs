namespace Nvx.ConsistentAPI.Tests;

public class StaticEndpointsIntegration
{
  [Fact(DisplayName = "Returns static data")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();

    foreach (var user in (string[]) ["admin", "cando", "mike", "bob", "tom"])
    {
      // Perform an action first as the user to ensure that the user security profile receives the username.
      _ = await setup.CurrentUser(asUser: user);
      var asUser = await setup.StaticEndpoint<MyStaticData>(asUser: user);
      Assert.NotNull(asUser);
      Assert.Equal(user, asUser.Name);
      Assert.Equal(user == "admin", asUser.IsAdmin);
      Assert.Equal(setup.Auth.ByName(user), asUser.Sub);
    }
  }
}
