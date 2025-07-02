namespace Nvx.ConsistentAPI.Tests;

public class EnumsIntegrationTests
{
  [Theory(DisplayName = "serializes, deserializes and filters enums")]
  [InlineData(OneEnum.Word)]
  [InlineData(OneEnum.MultipleWords)]
  [InlineData(OneEnum.EvenMoreWords)]
  public async Task SerializeAndDeserialize(OneEnum value)
  {
    await using var setup = await Initializer.Do();

    var result = await setup.Command(new SaveEntityWithEnum(value));
    var readModel = await setup.ReadModel<EntityWithEnumReadModel>(result.EntityId);
    Assert.Equal(value, readModel.EnumValue);
    var readModels = await setup.ReadModels<EntityWithEnumReadModel>();
    Assert.NotEmpty(readModels.Items);
    var filteredReadModelsEquals = await setup
      .ReadModels<EntityWithEnumReadModel>(
        queryParameters: new Dictionary<string, string[]> { { "eq-EnumValue", [value.ToString()] } });
    Assert.All(filteredReadModelsEquals.Items, rm => Assert.Equal(value, rm.EnumValue));
  }
}
