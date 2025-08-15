namespace Nvx.ConsistentAPI.Tests.Framework.Commands.Validation;

public class ValidationRulesTests
{
  [Fact(
    DisplayName = "uses the validation rules engine",
    Skip = "TODO: create a new entity with validation rules to test in isolation")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    await setup.Command(new SetValidationRule("create-product", "[\"error\"]"), true);
    var singleError = await setup.FailingCommand(new CreateProduct(Guid.NewGuid(), "banana", null), 400);
    Assert.Single(singleError.Errors);
    Assert.Equal("error", singleError.Errors[0]);
    await setup.Command(new SetValidationRule("create-product", "[\"error 2\"]"), true);
    await setup.Command(new SetValidationRule("create-product", "[\"error 3\"]"), true);
    var tripleError = await setup.FailingCommand(new CreateProduct(Guid.NewGuid(), "banana", null), 400);
    Assert.Equal(3, tripleError.Errors.Length);
    Assert.Equal("error", tripleError.Errors[0]);
    Assert.Equal("error 2", tripleError.Errors[1]);
    Assert.Equal("error 3", tripleError.Errors[2]);
    await setup.Command(new RemoveValidationRule("create-product", "[\"error\"]"), true);
    await setup.Command(new RemoveValidationRule("create-product", "[\"error 2\"]"), true);
    await setup.Command(new RemoveValidationRule("create-product", "[\"error 3\"]"), true);
    await setup.Command(new CreateProduct(Guid.NewGuid(), "banana", null), true);
  }
}
