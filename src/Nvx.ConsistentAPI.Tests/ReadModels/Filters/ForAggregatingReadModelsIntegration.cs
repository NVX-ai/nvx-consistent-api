namespace Nvx.ConsistentAPI.Tests.ReadModels.Filters;

public class ForAggregatingReadModelsIntegration
{
  [Fact(DisplayName = "should filter by array properties")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var productId = Guid.NewGuid();
    await setup.Command(new CreateProduct(productId, "product", null), true);
    await EventuallyConsistent.WaitForAggregation(async () =>
    {
      var readModel = await setup.ReadModel<AggregatingStockReadModel>(productId.ToString());
      Assert.NotNull(readModel);

      var readModels = await setup.ReadModels<AggregatingStockReadModel>(
        queryParameters: new Dictionary<string, string[]>
        {
          { "eq-ProductId", [productId.ToString()] },
          { "nia-MyStrings", ["a"] }
        });

      Assert.Empty(readModels.Items);
    });
  }
}
