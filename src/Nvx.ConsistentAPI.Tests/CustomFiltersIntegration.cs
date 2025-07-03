namespace Nvx.ConsistentAPI.Tests;

public class CustomFiltersIntegration
{
  [Fact(DisplayName = "joins on another table and filters as expected")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();

    var tenantId = Guid.NewGuid();
    await setup.Command(
      new AssignTenantPermission(setup.Auth.CandoSub, "admin"),
      true,
      tenantId
    );

    var stockedIngredientId = await setup
      .Command(new CreateIngredient())
      .Map(r => Guid.Parse(r.EntityId));
    await setup.Command(new SupplyIngredient(stockedIngredientId, 10));
    var outOfStockIngredientId = await setup
      .Command(new CreateIngredient())
      .Map(r => Guid.Parse(r.EntityId));
    var stockedPizzaId = await setup
      .Command(new CreatePizza([stockedIngredientId]), tenantId: tenantId)
      .Map(r => Guid.Parse(r.EntityId));
    var outOfStockPizzaId = await setup
      .Command(
        new CreatePizza([outOfStockIngredientId, stockedIngredientId]),
        tenantId: tenantId)
      .Map(r => Guid.Parse(r.EntityId));

    var singlePizzaPage = await setup.ReadModels<AvailablePizzaReadModel>(
      tenantId: tenantId,
      queryParameters: new Dictionary<string, string[]>
        { { "eq-Id", [stockedPizzaId.ToString(), outOfStockPizzaId.ToString()] } });
    Assert.Single(singlePizzaPage.Items);
    Assert.Equal(stockedPizzaId.ToString(), singlePizzaPage.Items.First().Id);
    await setup.ReadModel<AvailablePizzaReadModel>(stockedPizzaId.ToString(), tenantId: tenantId);
    await setup.ReadModelNotFound<AvailablePizzaReadModel>(outOfStockPizzaId.ToString(), tenantId);

    await setup.Command(new SupplyIngredient(outOfStockIngredientId, 10));

    var withIngredients = await setup.ReadModels<AvailablePizzaReadModel>(
      tenantId: tenantId,
      queryParameters: new Dictionary<string, string[]>
        { { "eq-Id", [stockedPizzaId.ToString(), outOfStockPizzaId.ToString()] } });
    Assert.Equal(2, withIngredients.Items.Count());

    // Test for custom filter on override tenant filter
    var tenantId2 = Guid.NewGuid();
    var stockedPizzaVisibleTenant2 = await setup
      .Command(
        new CreatePizza([stockedIngredientId]),
        tenantId: tenantId2,
        asAdmin: true)
      .Map(r => Guid.Parse(r.EntityId));
    var stockedPizzaNotVisibleTenant2 = await setup
      .Command(
        new CreatePizza([stockedIngredientId]),
        tenantId: tenantId2,
        asAdmin: true)
      .Map(r => Guid.Parse(r.EntityId));
    await setup
      .Command(
        new CreateExternalPizza(stockedPizzaVisibleTenant2),
        tenantId: tenantId,
        asAdmin: true)
      .Map(r => Guid.Parse(r.EntityId));

    var withCustomFilter = await setup.ReadModels<ExternalPizzaReadModel>(tenantId: tenantId);
    Assert.Contains(stockedPizzaVisibleTenant2, withCustomFilter.Items.Select(p => p.PizzaId));
    Assert.DoesNotContain(stockedPizzaNotVisibleTenant2, withCustomFilter.Items.Select(p => p.PizzaId));
  }
}
