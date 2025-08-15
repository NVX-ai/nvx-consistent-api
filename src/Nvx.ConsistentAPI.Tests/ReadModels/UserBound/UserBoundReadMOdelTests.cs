namespace Nvx.ConsistentAPI.Tests.ReadModels.UserBound;

public class UserBoundReadMOdelTests
{
  [Fact(DisplayName = "restricts access to read models to its user")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    await setup.Command(new RegisterFavoriteFood("pizza"), true);
    await setup.Command(new RegisterFavoriteFood("banana"));
    var adminFavoriteFoods = await setup.ReadModels<UserFavoriteFoodReadModel>(true);
    Assert.Contains(adminFavoriteFoods.Items, model => model.Name == "pizza");
    Assert.DoesNotContain(adminFavoriteFoods.Items, model => model.Name == "banana");
    var nonAdminFavoriteFoods = await setup.ReadModels<UserFavoriteFoodReadModel>();
    Assert.Contains(nonAdminFavoriteFoods.Items, model => model.Name == "banana");
    Assert.DoesNotContain(nonAdminFavoriteFoods.Items, model => model.Name == "pizza");
    await setup.ReadModel<UserFavoriteFoodReadModel>(setup.Auth.AdminSub, asAdmin: true);
    await setup.ReadModelNotFound<UserFavoriteFoodReadModel>(setup.Auth.AdminSub);
    await setup.ReadModel<UserFavoriteFoodReadModel>(setup.Auth.CandoSub, asAdmin: false);
    await setup.ReadModelNotFound<UserFavoriteFoodReadModel>(setup.Auth.CandoSub, asAdmin: true);
  }
}
