namespace Nvx.ConsistentAPI.Tests.ReadModels;

public class UserBoundReadModelsTest
{
  [Fact(DisplayName = "Only the owner of a user bound read model can query it")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    await setup.Command(new RegisterFavoriteFood("pizza"), true);
    await setup.Command(new RegisterFavoriteFood("banana"));
    var adminFavoriteFoods = await setup.ReadModels<UserFavoriteFoodReadModel>(true);
    Assert.Equal(1, adminFavoriteFoods.Total);
    Assert.Contains(adminFavoriteFoods.Items, model => model.Name == "pizza");
    var nonAdminFavoriteFoods = await setup.ReadModels<UserFavoriteFoodReadModel>();
    Assert.Equal(1, nonAdminFavoriteFoods.Total);
    Assert.Contains(nonAdminFavoriteFoods.Items, model => model.Name == "banana");
    await setup.ReadModel<UserFavoriteFoodReadModel>(setup.Auth.AdminSub, asAdmin: true);
    await setup.ReadModelNotFound<UserFavoriteFoodReadModel>(setup.Auth.AdminSub);
    await setup.ReadModel<UserFavoriteFoodReadModel>(setup.Auth.CandoSub, asAdmin: false);
    await setup.ReadModelNotFound<UserFavoriteFoodReadModel>(setup.Auth.CandoSub, asAdmin: true);
  }
}
