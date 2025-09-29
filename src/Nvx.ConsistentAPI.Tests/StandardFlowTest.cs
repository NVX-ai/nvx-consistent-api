// ReSharper disable AccessToDisposedClosure

using System.Net;
using Newtonsoft.Json;

namespace Nvx.ConsistentAPI.Tests;

public class StandardFlowTest
{
  [Fact(DisplayName = "The application handles all standard functionality")]
  public async Task Test1()
  {
    await using var setup = await Initializer.Do();

    var productId = Guid.NewGuid();
    var productName = $"{productId.ToString().ToLowerInvariant()} product";

    var uploadResult = await setup.Upload();
    await setup.DownloadAndCompare(uploadResult.EntityId.Apply(Guid.Parse), "banana");
    _ = await setup.CurrentUser(asUser: setup.Auth.ByName("john"));

    await UserBoundReadModels();
    await Notifications();

    await setup.Command(new AssignApplicationPermission(setup.Auth.CandoSub, "product-creator"), true);
    await setup.UnauthorizedCommand(new CreateProduct(Guid.NewGuid(), "banana", null));
    await setup.ForbiddenCommand(new CreateProduct(Guid.NewGuid(), "banana", null));
    await setup.ForbiddenReadModel<UserWithPermissionReadModel>();

    await setup.FailingCommand(new CommandThatLikesAdmins(), 409, asAdmin: true);
    await setup.ForbiddenCommand(new CommandThatLikesAdmins());

    // Validation rule
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
    var bananaId = Guid.NewGuid();
    await setup.Command(new CreateProduct(bananaId, "banana", null));
    await setup.WaitForTodo(bananaId.ToString(), "announce-new-product");

    // Basic command handling and entity projection.
    await setup.ReadModelNotFound<ProductStock>(productId.ToString());
    await Enumerable
      .Range(0, 20)
      .Select<int, Func<Task<Unit>>>(_ => async () =>
      {
        await setup.Command(new AddStock(productId, 5));
        return unit;
      })
      .Parallel();

    await setup.WaitFor(new StockAdded(productId, 5), 20);

    const string validTag1 = "Cosmetics";
    const string validTag2 = "Food";
    await setup.Command(new AddStockTags(productId, [validTag1, validTag2, null]));
    var unknownProductStock = await setup.ReadModel<ProductStock>(productId.ToString());

    Assert.Equal(productId.ToString(), unknownProductStock.Id);
    Assert.Equal(100, unknownProductStock.Amount);
    Assert.Equal("Unknown product", unknownProductStock.Name);

    // Validate the null tag is ignored - Read Model is clean. 
    Assert.Equal(2, unknownProductStock.Tags.Length);
    Assert.Contains(unknownProductStock.Tags, tag => tag.Equals(validTag1));
    Assert.Contains(unknownProductStock.Tags, tag => tag.Equals(validTag2));

    // This creates a lot of products, so the processors can start working and be checked at the end. The processor
    // fails occasionally, and it takes at least 25 seconds to recover, so this starts now and is verified at the end.
    // The test is technically flaky, but the chances of failure are abyssal.
    var wordInTheProductName = Guid.NewGuid().ToString().ToLowerInvariant();
    var productIds = Enumerable.Range(0, 98).Select(_ => Guid.NewGuid()).ToArray();
    await productIds
      .Select<Guid, Func<Task<Unit>>>(id => async () =>
      {
        await setup.Command(
          new CreateProduct(id, $"{wordInTheProductName} {productId} {Guid.NewGuid()}", null));
        return unit;
      })
      .Parallel(25);

    // Concurrency handling, there's only 100 stock, so only 40 should succeed.
    var results = await Enumerable
      .Range(0, 60)
      .Select<int, Func<Task<bool>>>(_ => async () =>
      {
        try
        {
          await setup.Command(new RetrieveStock(productId, 2));
          return true;
        }
        catch
        {
          return false;
        }
      })
      .Parallel(35);

    Assert.Equal(50, results.Count(Id));

    var productWithNoStock = await setup.ReadModel<ProductStock>(productId.ToString());
    Assert.Equal(productId.ToString(), productWithNoStock.Id);
    Assert.Equal(0, productWithNoStock.Amount);
    Assert.Equal("Unknown product", productWithNoStock.Name);

    // Validations
    await setup.FailingCommand(new RetrieveStock(productId, 1), 409);

    var result = await setup.Command(new CreateProduct(productId, productName, null));
    Assert.Equal(productId.ToString(), result.EntityId);

    await setup.FailingCommand(
      new AddProductPicture(productId, new AttachedFile(Guid.NewGuid(), null)),
      (int)HttpStatusCode.Conflict);
    await setup.Command(
      new AddProductPicture(productId, new AttachedFile(uploadResult.EntityId.Apply(Guid.Parse), null)));

    // Verify the background runners.
    var readModel = await setup.ReadModel<UserRegistryOfNamedProductsReadModel>(
      setup.Auth.CandoSub);
    Assert.True(100 <= readModel.Count, $"Expecting at least 100 products, got {readModel.Count}");

    await setup.FailingCommand(new CreateProduct(productId, productName, null), 409);

    // Eventual consistency (duh).
    var firstPage = await setup
      .ReadModels<ProductStock>(
        queryParameters: new Dictionary<string, string[]> { { "ts-Name", [productId.ToString()] } });
    Assert.NotEmpty(firstPage.Items);
    Assert.Equal(99, firstPage.Total);
    var productStock = await setup.ReadModel<ProductStock>(productId.ToString());
    Assert.Equal(productId.ToString(), productStock.Id);
    Assert.Equal(productId, productStock.ProductId);
    Assert.Equal(productName, productStock.Name);
    Assert.Equal(0, productStock.Amount);
    Assert.Equal(uploadResult.EntityId.Apply(Guid.Parse), productStock.PictureId);
    Assert.Equal(42, productStock.LongNumber);
    Assert.Equal(3.1416f, productStock.AllFloat);
    Assert.Equal(0.1, productStock.YourBlessings);
    Assert.True(productStock.IngForColumbine);
    Assert.Equal('a', productStock.Mander);
    DateTime.SpecifyKind(new DateTime(2017, 1, 1), DateTimeKind.Utc).IsCloseTo(productStock.Beginning);
    DateTime.SpecifyKind(new DateTime(2017, 1, 1), DateTimeKind.Utc).IsCloseTo(productStock.MaybeBeginning);
    Assert.Equal(new DateTimeOffset(2017, 1, 1, 1, 1, 1, TimeSpan.Zero), productStock.Offset);
    Assert.Equal(new DateTimeOffset(2017, 1, 1, 1, 1, 1, TimeSpan.Zero), productStock.MaybeOffset);
    Assert.Equal(new TimeOnly(1, 1, 1), productStock.TimeOnly);
    Assert.Equal(new TimeOnly(1, 1, 1), productStock.MaybeTimeOnly);

    // Filtering
    var firstPageFiltered = await setup.ReadModels<ProductStock>(
      queryParameters: new Dictionary<string, string[]>
        { { "pageSize", ["35"] }, { "ts-Name", [productId.ToString().ToUpper()] } });
    Assert.Equal(35, firstPageFiltered.Items.Count());
    Assert.NotEmpty(firstPageFiltered.Items);
    Assert.Equal(99, firstPageFiltered.Total);
    var ids = firstPageFiltered.Items.Take(25).Select(i => i.Id).ToArray();
    var filteredPage =
      await setup.ReadModels<ProductStock>(queryParameters: new Dictionary<string, string[]> { { "eq-Id", ids } });
    Assert.Equal(25, filteredPage.Total);
    var filteredAndSizeConstrained = await setup
      .ReadModels<ProductStock>(
        queryParameters: new Dictionary<string, string[]> { { "eq-Id", ids }, { "pageSize", ["13"] } });
    Assert.Equal(25, filteredAndSizeConstrained.Total);
    Assert.Equal(13, filteredAndSizeConstrained.Items.Count());

    var adminUser = await setup.ReadModel<UserWithPermissionReadModel>(
      new UserWithPermissionId(setup.Auth.AdminSub, "admin").StreamId(),
      asAdmin: true);
    Assert.Equal("admin", adminUser.Name);
    var canDoUser = await setup.ReadModel<UserWithPermissionReadModel>(
      new UserWithPermissionId(setup.Auth.CandoSub, "product-creator").StreamId(),
      asAdmin: true);
    Assert.Equal("cando", canDoUser.Name);
    Assert.Equal("cando@testdomain.com", canDoUser.Email);

    await productIds
      .Select<Guid, Func<Task<Unit>>>(id => async () =>
      {
        await setup.WaitForTodo(id.ToString(), "announce-new-product");
        return unit;
      })
      .Parallel();

    var aggregated1 = await setup
      .ReadModels<AggregatingStockReadModel>(
        queryParameters: new Dictionary<string, string[]> { { "ts-Name", [wordInTheProductName] } });
    Assert.Equal(50, aggregated1.Items.Count());
    Assert.Equal(98, aggregated1.Total);

    var otherProductId = Guid.Parse(aggregated1.Items.First().Id);
    await setup.Command(new HideAggregatingProduct(otherProductId));

    var afterHiding = await setup
      .ReadModels<AggregatingStockReadModel>(
        queryParameters: new Dictionary<string, string[]> { { "ts-Name", [wordInTheProductName] } });
    Assert.Equal(50, afterHiding.Items.Count());
    Assert.Equal(97, afterHiding.Total);

    await setup.Command(new ShowAggregatingProduct(otherProductId));

    var afterShowingAgain = await setup
      .ReadModels<AggregatingStockReadModel>(
        queryParameters: new Dictionary<string, string[]> { { "ts-Name", [wordInTheProductName] } });
    Assert.Equal(50, afterShowingAgain.Items.Count());
    Assert.Equal(98, afterShowingAgain.Total);

    // Ingestor
    var ingestedProductId = Guid.NewGuid();
    await setup.Ingest<ProductNameIngestor>(
      JsonConvert.SerializeObject(new ProductNameToBeIngested("Blah", ingestedProductId)));

    var ingestedProduct = await setup.ReadModel<ProductStock>(
      ingestedProductId.ToString());
    Assert.Equal("Blah", ingestedProduct.Name);

    return;

    async Task Notifications()
    {
      var message = Guid.NewGuid().ToString();
      await setup.Command(new SendNotificationToUser(message, setup.Auth.ByName("john")), true);
      var notifications = await setup.ReadModels<UserNotificationReadModel>(asUser: "john");
      Assert.Single(notifications.Items);
      var notification = notifications.Items.First();
      Assert.Equal(message, notification.Message);
      Assert.False(notification.IsRead);
      Assert.Equal("banana", notification.AdditionalDetails.GetValueOrDefault("banana"));
    }

    async Task UserBoundReadModels()
    {
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
}
