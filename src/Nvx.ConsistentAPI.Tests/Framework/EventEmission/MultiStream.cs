namespace Nvx.ConsistentAPI.Tests.Framework.EventEmission;

public class MultiStream
{
  [Fact(DisplayName = "Multi stream emission allows to emit to other streams")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var productId = await setup
      .Command(new CreateProduct(Guid.NewGuid(), $"Test Product{Guid.NewGuid()}", null), true)
      .Map(m => m.EntityId)
      .Map(Guid.Parse);

    var storeIds = Enumerable.Range(0, 15).Select(_ => Guid.NewGuid()).ToArray();

    await setup.Command(new SendProductToStores(productId, storeIds));

    foreach (var storeId in storeIds)
    {
      var model = await setup.ReadModel<ProductStoreFrontReadModel>(
        new StoreFrontProductId(storeId, productId).ToString());
      Assert.Equal(productId, model.ProductId);
      Assert.Equal(storeId, model.StoreId);
    }
  }
}
