namespace Nvx.ConsistentAPI.Tests;

public class DefaultedReadModelIntegrationTests
{
  [Fact(DisplayName = "Have the defaulted read model return a default value when not found")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var id = Guid.NewGuid();
    var defaultedReadModel = await setup.ReadModel<DefaultedReadModelReadModel>(id.ToString());
    Assert.Equal("This was defaulted", defaultedReadModel.SomeText);
    await setup.Command(new DoSomethingToDefaultedReadModel(id));
    await EventuallyConsistent.WaitFor(async () =>
    {
      var model = await setup.ReadModel<DefaultedReadModelReadModel>(id.ToString());
      Assert.Equal("This is not default", model.SomeText);
    });
  }
}
