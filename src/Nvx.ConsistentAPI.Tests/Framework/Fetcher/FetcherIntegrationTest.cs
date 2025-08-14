namespace Nvx.ConsistentAPI.Tests.Framework.Fetcher;

public class FetcherIntegrationTest
{
  [Fact(DisplayName = "should fetch the latest state of a product")]
  public async Task Test1()
  {
    await using var setup = await Initializer.Do();
    var productId = Guid.NewGuid();
    await setup.InsertEvents(new ProductCreated(productId, $"Product {productId}"));
    var product = await setup.Fetcher.Fetch<Product>(new StrongGuid(productId));
    product.Ent.ShouldBeSome();
    var nonExistingProduct = await setup.Fetcher.Fetch<Product>(new StrongGuid(Guid.NewGuid()));
    nonExistingProduct.Ent.ShouldBeNone();
  }
}
