using System.Data.Common;
using Dapper;
using EventStore.Client;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Nvx.ConsistentAPI;
using Nvx.ConsistentAPI.Framework.StaticEndpoints;

namespace TestEventModel;

// Commands
public record AddStock(Guid ProductId, int Amount) : EventModelCommand<Stock>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(ProductId);

  public Result<EventInsertion, ApiError> Decide(
    Option<Stock> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new AnyState(new StockAdded(ProductId, Amount));

  public IEnumerable<string> Validate()
  {
    if (Amount < 1)
    {
      yield return "Amount can't be smaller than one.";
    }
  }
}

public record RetrieveStock(Guid ProductId, int Amount) : EventModelCommand<Stock>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(ProductId);

  public Result<EventInsertion, ApiError> Decide(
    Option<Stock> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    Amount <= entity.Map(e => e.Amount).DefaultValue(0)
      ? new ExistingStream(new StockRetrieved(ProductId, Amount))
      : new ConflictError("There was not enough stock");

  public IEnumerable<string> Validate()
  {
    if (Amount < 1)
    {
      yield return "Amount can't be smaller than one.";
    }
  }
}

// ReSharper disable once NotAccessedPositionalProperty.Global
public record CreateProduct(Guid ProductId, string Name, AttachedFile? Photo) : EventModelCommand<Product>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(ProductId);

  public Result<EventInsertion, ApiError> Decide(
    Option<Product> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    entity.Match<Result<EventInsertion, ApiError>>(
      _ => new ConflictError("Tried to create an already existing product"),
      () => new CreateStream(new ProductCreated(ProductId, Name))
    );

  public IEnumerable<string> Validate() => [];
}

public record SendProductToStores(Guid ProductId, Guid[] StoreIds) : EventModelCommand<StoreFrontProduct>
{
  public static EventModelingCommandArtifact Definition =>
    new CommandDefinition<SendProductToStores, StoreFrontProduct> { AreaTag = "Stores" };

  public Result<EventInsertion, ApiError> Decide(
    Option<StoreFrontProduct> entity,
    Option<UserSecurity> user,
    FileUpload[] files) =>
    new MultiStream(StoreIds.Select(sid => new StoreReceivedProduct(sid, ProductId)).ToArray<EventModelEvent>());

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => None;
}

public record AddProductPicture(Guid ProductId, AttachedFile File) : EventModelCommand<Product>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(ProductId);

  public Result<EventInsertion, ApiError> Decide(
    Option<Product> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new AnyState(new ProductPictureAdded(ProductId, File.Id));

  public IEnumerable<string> Validate() => [];
}

public record RegisterOrganizationBuilding(string Name) : TenantEventModelCommand<OrganizationBuilding>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(Name);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<OrganizationBuilding> entity,
    UserSecurity user,
    FileUpload[] files
  ) =>
    entity.Match<Result<EventInsertion, ApiError>>(
      _ => new ConflictError("Tried to create an organization building that already existed"),
      () => new CreateStream(new OrganizationBuildingRegistered(Name, tenantId))
    );

  public IEnumerable<string> Validate() => [];
}

public record RegisterFavoriteFood(string Name) : EventModelCommand<UserFavoriteFood>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<UserFavoriteFood> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    user.Match<Result<EventInsertion, ApiError>>(
      u => new AnyState(new UserSelectedFavoriteFood(u.Sub, Name)),
      () => new ConflictError("You can't select favorite food if you are not logged in.")
    );

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Name);
  public IEnumerable<string> Validate() => [];
}

public record CommandThatLikesAdmins : EventModelCommand<UserFavoriteFood>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<UserFavoriteFood> entity,
    Option<UserSecurity> user,
    FileUpload[] files) =>
    new ConflictError("This command will be a conflict if you are an admin, but forbidden if you are not");

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => None;
  public IEnumerable<string> Validate() => [];
}

public record HideAggregatingProduct(Guid ProductId) : EventModelCommand<Product>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(ProductId);

  public Result<EventInsertion, ApiError> Decide(
    Option<Product> entity,
    Option<UserSecurity> user,
    FileUpload[] files) => new AnyState(new AggregatingProductHidden(ProductId));

  public IEnumerable<string> Validate() => [];
}

public record ShowAggregatingProduct(Guid ProductId) : EventModelCommand<Product>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(ProductId);

  public Result<EventInsertion, ApiError> Decide(
    Option<Product> entity,
    Option<UserSecurity> user,
    FileUpload[] files) => new AnyState(new AggregatingProductShown(ProductId));

  public IEnumerable<string> Validate() => [];
}

public record SendNotificationToUser(string Message, string RecipientSub) : EventModelCommand<UserNotificationEntity>
{
  public static readonly EventModelingCommandArtifact Definition =
    new CommandDefinition<SendNotificationToUser, UserNotificationEntity> { AreaTag = "Notifications" };

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => None;

  public Result<EventInsertion, ApiError> Decide(
    Option<UserNotificationEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) =>
    new AnyState(
      new NotificationSent(
        Guid.NewGuid().ToString(),
        RecipientSub,
        Message,
        "direct-send",
        null,
        null,
        user.Match<string?>(u => u.Sub, () => null),
        DateTime.UtcNow,
        new Dictionary<string, string> { { "banana", "banana" } }
      )
    );
}

// Events
public record StockAdded(Guid ProductId, int Amount) : EventModelEvent
{
  public string SwimLane => Stock.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(ProductId);
}

public record StockRetrieved(Guid ProductId, int Amount) : EventModelEvent
{
  public string SwimLane => Stock.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(ProductId);
}

public record StockNamed(Guid ProductId, string Name) : EventModelEvent
{
  public string SwimLane => Stock.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(ProductId);
}

public record ProductCreated(Guid ProductId, string Name) : EventModelEvent
{
  public string SwimLane => Product.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(ProductId);
}

public record ProductPictureAdded(Guid ProductId, Guid PictureId) : EventModelEvent
{
  public string SwimLane => Product.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(ProductId);
}

public record AggregatingProductHidden(Guid ProductId) : EventModelEvent
{
  public string SwimLane => Product.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(ProductId);
}

public record AggregatingProductShown(Guid ProductId) : EventModelEvent
{
  public string SwimLane => Product.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(ProductId);
}

public record StockPictureAdded(Guid ProductId, Guid PictureId) : EventModelEvent
{
  public string SwimLane => Stock.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(ProductId);
}

public record OrganizationBuildingRegistered(string Name, Guid TenantId) : EventModelEvent
{
  public string SwimLane => OrganizationBuilding.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Name);
}

public record UserSelectedFavoriteFood(string Sub, string Name) : EventModelEvent
{
  public string SwimLane => UserFavoriteFood.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record StoreReceivedProduct(Guid StoreId, Guid ProductId) : EventModelEvent
{
  public string SwimLane => StoreFrontProduct.StreamPrefix;
  public StrongId GetEntityId() => new StoreFrontProductId(StoreId, ProductId);
}

public record UserNamedOneProduct(string UserSub, Guid ProductId, string Name) : EventModelEvent
{
  public string SwimLane => UserRegistryOfNamedProducts.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(UserSub);
}

// Entities
// ReSharper disable once NotAccessedPositionalProperty.Global
public partial record Product(Guid Id, string Name, Guid? PictureId) :
  EventModelEntity<Product>,
  Folds<ProductCreated, Product>,
  Folds<ProductPictureAdded, Product>,
  Folds<AggregatingProductShown, Product>,
  Folds<AggregatingProductHidden, Product>
{
  public const string StreamPrefix = "entity-product-";
  public string EntityId => Id.ToString();
  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<Product> Fold(
    AggregatingProductHidden evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this);

  public ValueTask<Product> Fold(
    AggregatingProductShown evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this);

  public ValueTask<Product> Fold(ProductCreated evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Name = evt.Name });

  public ValueTask<Product> Fold(ProductPictureAdded evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { PictureId = evt.PictureId });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";
  public static Product Defaulted(StrongGuid id) => new(id.Value, "Unknown product", null);
}

public partial record Stock(Guid ProductId, int Amount, string ProductName, Guid? PictureId)
  : EventModelEntity<Stock>,
    Folds<StockAdded, Stock>,
    Folds<StockRetrieved, Stock>,
    Folds<StockNamed, Stock>,
    Folds<StockPictureAdded, Stock>
{
  public const string StreamPrefix = "entity-stock-";
  public string GetStreamName() => GetStreamName(ProductId);

  public ValueTask<Stock> Fold(StockAdded sa, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Amount = Amount + sa.Amount });

  public ValueTask<Stock> Fold(StockNamed sn, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { ProductName = sn.Name });

  public ValueTask<Stock> Fold(StockPictureAdded evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { PictureId = evt.PictureId });

  public ValueTask<Stock> Fold(StockRetrieved rs, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Amount = Amount - rs.Amount });

  public static string GetStreamName(Guid productId) => $"{StreamPrefix}{productId}";
  public static Stock Defaulted(StrongGuid id) => new(id.Value, 0, "Unknown product", null);
}

public record StoreFrontProductId(Guid StoreId, Guid ProductId) : StrongId
{
  public override string StreamId() => $"{StoreId}-{ProductId}";
  public override string ToString() => StreamId();
}

public partial record StoreFrontProduct(Guid StoreId, Guid ProductId)
  : EventModelEntity<StoreFrontProduct>, Folds<StoreReceivedProduct, StoreFrontProduct>
{
  public const string StreamPrefix = "store-front-product-";

  public static readonly EntityDefinition Definition = new EntityDefinition<StoreFrontProduct, StoreFrontProductId>
  {
    Defaulter = Defaulted,
    StreamPrefix = StreamPrefix
  };

  public string GetStreamName() => GetStreamName(GetStreamId());

  public ValueTask<StoreFrontProduct> Fold(StoreReceivedProduct evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this);

  public StoreFrontProductId GetStreamId() => new(StoreId, ProductId);

  public static string GetStreamName(StoreFrontProductId id) => $"{StreamPrefix}{id}";
  public static StoreFrontProduct Defaulted(StoreFrontProductId id) => new(id.StoreId, id.ProductId);
}

public partial record OrganizationBuilding(string EntityId, Guid TenantId)
  : EventModelEntity<OrganizationBuilding>,
    Folds<OrganizationBuildingRegistered, OrganizationBuilding>
{
  public const string StreamPrefix = "organization-building-";

  public string GetStreamName() => GetStreamName(EntityId);

  public ValueTask<OrganizationBuilding> Fold(
    OrganizationBuildingRegistered obr,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(new OrganizationBuilding(obr.Name, obr.TenantId));

  public static OrganizationBuilding Defaulted(StrongString name) =>
    new(name.Value, Guid.Empty);

  public static string GetStreamName(string name) => $"{StreamPrefix}{name}";
}

public partial record UserFavoriteFood(string Sub, string FavoriteFood)
  : EventModelEntity<UserFavoriteFood>,
    Folds<UserSelectedFavoriteFood, UserFavoriteFood>
{
  public const string StreamPrefix = "user-favorite-food-";
  public string GetStreamName() => GetStreamName(Sub);

  public ValueTask<UserFavoriteFood> Fold(
    UserSelectedFavoriteFood evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { FavoriteFood = evt.Name });

  public static string GetStreamName(string sub) => $"{StreamPrefix}{sub}";
  public static UserFavoriteFood Defaulted(StrongString sub) => new(sub.Value, string.Empty);
}

public partial record UserRegistryOfNamedProducts(string UserSub, int Count)
  : EventModelEntity<UserRegistryOfNamedProducts>,
    Folds<UserNamedOneProduct, UserRegistryOfNamedProducts>
{
  public const string StreamPrefix = "user-registry-of-named-products-";

  public static readonly EntityDefinition Definition = new EntityDefinition<UserRegistryOfNamedProducts, StrongString>
  {
    Defaulter = Defaulted,
    StreamPrefix = StreamPrefix
  };

  public string GetStreamName() => GetStreamName(UserSub);

  public ValueTask<UserRegistryOfNamedProducts> Fold(
    UserNamedOneProduct evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Count = Count + 1 });

  public static string GetStreamName(string sub) => $"{StreamPrefix}{sub}";
  private static UserRegistryOfNamedProducts Defaulted(StrongString sub) => new(sub.Value, 0);
}

// Read models
// ReSharper disable NotAccessedPositionalProperty.Global
public record ProductStock(
  string Id,
  Guid ProductId,
  string Name,
  int Amount,
  Guid? PictureId,
  long LongNumber,
  float AllFloat,
  double YourBlessings,
  bool IngForColumbine,
  char Mander,
  DateTime Beginning,
  DateTime? MaybeBeginning,
  DateTimeOffset Offset,
  DateTimeOffset? MaybeOffset,
  TimeOnly TimeOnly,
  TimeOnly? MaybeTimeOnly) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongGuid(ProductId);
}
// ReSharper restore NotAccessedPositionalProperty.Global

public record UserFavoriteFoodReadModel(
  string Id,
  string Name,
  string UserSub
) : EventModelReadModel, UserBound
{
  public StrongId GetStrongId() => new StrongString(UserSub);
}

public record OrganizationBuildingReadModel(string Name, Guid TenantId, string Id) : EventModelReadModel, IsTenantBound
{
  public StrongId GetStrongId() => new StrongString(Name);
}

public record ProductStoreFrontReadModel(string Id, Guid StoreId, Guid ProductId) : EventModelReadModel
{
  public static readonly EventModelingReadModelArtifact Definition =
    new ReadModelDefinition<ProductStoreFrontReadModel, StoreFrontProduct>
    {
      StreamPrefix = StoreFrontProduct.StreamPrefix,
      Projector = sfp => [new ProductStoreFrontReadModel(sfp.GetStreamId().ToString(), sfp.StoreId, sfp.ProductId)],
      AreaTag = "Stores"
    };

  public StrongId GetStrongId() => new StoreFrontProductId(StoreId, ProductId);
}

public record AccessoryStockTable(string Id, string SomeText) : HasId;

// ReSharper disable NotAccessedPositionalProperty.Global
public record AggregatingStockReadModel(
  string Id,
  Guid ProductId,
  string Name,
  int Amount,
  Guid? PictureId,
  string[] MyStrings,
  Guid[] MyGuids,
  int[] MyInts) :
  EventModelReadModel,
  IsSoftDeleted
// ReSharper restore NotAccessedPositionalProperty.Global
{
  public static readonly Guid[] Guids =
    [Guid.Parse("42E95437-3601-4FBA-B81D-18BDC0E90316"), Guid.Parse("3A11C012-480A-4831-98F2-463B9DAD4BB9")];

  // ReSharper disable once UnusedMember.Global
  public StrongId GetStrongId() => new StrongGuid(ProductId);

  public static IEnumerable<ReadModelAggregator> GetAggregators()
  {
    yield return new ProductCreatedAggregator();
    yield return new ProductStockAddedAggregator();
    yield return new ProductPictureAddedAggregator();
    yield return new AggregatingProductHiddenAggregator();
    yield return new AggregatingProductShownAggregator();
  }

  public static IEnumerable<string> GetStreamPrefixes()
  {
    yield return Product.StreamPrefix;
    yield return Stock.StreamPrefix;
  }

  private class ProductCreatedAggregator : ReadModelAggregator<ProductCreated>
  {
    protected override async Task<string[]> Aggregate(
      EventWithMetadata<ProductCreated> evt,
      Fetcher fetcher,
      DbConnection dbConnection,
      DbTransaction dbTransaction,
      TableDetails tableDetails)
    {
      await dbConnection.ExecuteAsync(
        tableDetails.UpsertSql,
        new AggregatingStockReadModel(
          evt.Event.ProductId.ToString(),
          evt.Event.ProductId,
          evt.Event.Name,
          0,
          null,
          ["a", "b"],
          Guids,
          [1, 2]),
        dbTransaction);
      return [evt.Event.ProductId.ToString()];
    }
  }

  private class ProductStockAddedAggregator : ReadModelAggregator<StockAdded>
  {
    protected override async Task<string[]> Aggregate(
      EventWithMetadata<StockAdded> evt,
      Fetcher fetcher,
      DbConnection dbConnection,
      DbTransaction dbTransaction,
      TableDetails tableDetails)
    {
      await dbConnection.ExecuteAsync(
        tableDetails.UpsertSql,
        new AggregatingStockReadModel(
          evt.Event.ProductId.ToString(),
          evt.Event.ProductId,
          "Unknown product",
          evt.Event.Amount,
          null,
          ["a", "b"],
          Guids,
          [1, 2]),
        dbTransaction);
      return [evt.Event.ProductId.ToString()];
    }
  }

  private class ProductPictureAddedAggregator : ReadModelAggregator<ProductPictureAdded>
  {
    protected override async Task<string[]> Aggregate(
      EventWithMetadata<ProductPictureAdded> evt,
      Fetcher fetcher,
      DbConnection dbConnection,
      DbTransaction dbTransaction,
      TableDetails tableDetails)
    {
      var sql = $"""
                 MERGE {tableDetails.TableName} AS Target
                 USING (SELECT @Id, @ProductId, @Name, @Amount, @PictureId) AS Source (Id, ProductId, Name, Amount, PictureId)
                 ON Target.Id = Source.Id
                 WHEN MATCHED THEN
                  UPDATE SET PictureId = Source.PictureId
                 WHEN NOT MATCHED THEN
                  INSERT (Id, ProductId, Name, Amount, PictureId)
                  VALUES (Source.Id, Source.ProductId, Source.Name, Source.Amount, Source.PictureId);
                 """;

      await dbConnection.ExecuteAsync(
        sql,
        new AggregatingStockReadModel(
          evt.Event.ProductId.ToString(),
          evt.Event.ProductId,
          "Unknown product",
          0,
          evt.Event.PictureId,
          ["a", "b"],
          Guids,
          [1, 2]),
        dbTransaction);
      return [evt.Event.ProductId.ToString()];
    }
  }

  private class AggregatingProductHiddenAggregator : ReadModelAggregator<AggregatingProductHidden>
  {
    protected override async Task<string[]> Aggregate(
      EventWithMetadata<AggregatingProductHidden> evt,
      Fetcher fetcher,
      DbConnection dbConnection,
      DbTransaction dbTransaction,
      TableDetails tableDetails)
    {
      await dbConnection.ExecuteAsync(
        $"UPDATE [{tableDetails.TableName}] SET [IsDeleted] = 1 WHERE Id = @Id;",
        new { Id = evt.Event.ProductId },
        dbTransaction);
      return [];
    }
  }

  private class AggregatingProductShownAggregator : ReadModelAggregator<AggregatingProductShown>
  {
    protected override async Task<string[]> Aggregate(
      EventWithMetadata<AggregatingProductShown> evt,
      Fetcher fetcher,
      DbConnection dbConnection,
      DbTransaction dbTransaction,
      TableDetails tableDetails)
    {
      await dbConnection.ExecuteAsync(
        $"UPDATE [{tableDetails.TableName}] SET [IsDeleted] = 0 WHERE Id = @Id;",
        new { Id = evt.Event.ProductId },
        dbTransaction);
      return [evt.Event.ProductId.ToString()];
    }
  }
}

public record UserRegistryOfNamedProductsReadModel(string Id, string UserSub, int Count) : EventModelReadModel
{
  public static readonly EventModelingReadModelArtifact Definition =
    new ReadModelDefinition<UserRegistryOfNamedProductsReadModel, UserRegistryOfNamedProducts>
    {
      StreamPrefix = UserRegistryOfNamedProducts.StreamPrefix,
      Projector = e => [new UserRegistryOfNamedProductsReadModel(e.UserSub, e.UserSub, e.Count)],
      AreaTag = "UserProfile"
    };

  public StrongId GetStrongId() => new StrongString(UserSub);
}

// Tasks
// ReSharper disable NotAccessedPositionalProperty.Global
public record ProductNamedTaskData(Guid Id, string Name, string UserSub) : TodoData;
// ReSharper restore NotAccessedPositionalProperty.Global

// Ingestors
public class ProductNameIngestor : Ingestor
{
  public async Task<Option<EventModelEvent>> Ingest(HttpContext context, Fetcher fetcher)
  {
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var data = JsonConvert.DeserializeObject<ProductNameToBeIngested>(body)!;
    context.Response.StatusCode = 200;
    await context.Response.WriteAsJsonAsync(new { });
    return new ProductCreated(data.ProductId, data.Name);
  }

  public string AreaTag => "ProductStock";
}

public record ProductNameToBeIngested(string Name, Guid ProductId);

public static class TestEventModel
{
  private static readonly Random Random = new();

  public static EventModel GetModel()
  {
    var initTime = DateTime.UtcNow;
    var task = new TodoTaskDefinition<ProductNamedTaskData, Product, ProductCreated, StrongGuid>
    {
      Type = "announce-new-product",
      Action = (d, _, _, _, _) =>
      {
        // Fail 3% of the time.
        if (Random.Next() % 33 == 0)
        {
          throw new Exception("Kaboom");
        }

        return Task.FromResult(
          First<EventInsertion, TodoOutcome>(new AnyState(new UserNamedOneProduct(d.UserSub, d.Id, d.UserSub))));
      },
      Originator = (created, _, md) => new ProductNamedTaskData(
        created.ProductId,
        created.Name,
        md.RelatedUserSub ?? string.Empty),
      SourcePrefix = Product.StreamPrefix,
      LockLength = TimeSpan.FromSeconds(4),
      Delay = TimeSpan.FromMilliseconds(1),
      Expiration = TimeSpan.FromMinutes(10)
    };

    var model = new EventModel
    {
      Commands =
      [
        new CommandDefinition<AddStock, Stock>
        {
          Description =
            "Adds stock to an existing product, since is meant to be used after adding the product to the shelves, it will always be accepted.",
          AreaTag = "ProductStock"
        },
        new CommandDefinition<CreateProduct, Product>
        {
          Description = "Adds a new product to the catalogue.",
          Auth = new PermissionsRequireOne("product-creator"),
          UsesValidationRules = true,
          AreaTag = "ProductStock"
        },
        new CommandDefinition<RetrieveStock, Stock>
        {
          Description =
            "Reduces the stock of a product, will be rejected if the amount greater than the available stock.",
          AreaTag = "ProductStock"
        },
        new CommandDefinition<RegisterOrganizationBuilding, OrganizationBuilding> { AreaTag = "OrganizationBuilding" },
        new CommandDefinition<AddProductPicture, Product> { AreaTag = "ProductStock" },
        new CommandDefinition<HideAggregatingProduct, Product> { AreaTag = "ProductStock" },
        new CommandDefinition<ShowAggregatingProduct, Product> { AreaTag = "ProductStock" },
        new CommandDefinition<RegisterFavoriteFood, UserFavoriteFood> { AreaTag = "FavoriteFood" },
        // This is silly, as we already have the RequirePermission possibility in the auth parameter, but it's just meant
        // to test the custom authorization.
        new CommandDefinition<CommandThatLikesAdmins, UserFavoriteFood>
        {
          Auth = new EveryoneAuthenticated(),
          CustomAuthorization =
            (user, _, _, _, _) => user.Match(u => u.ApplicationPermissions.Contains("admin"), () => true),
          AreaTag = "CustomAuthTest"
        },
        SendProductToStores.Definition,
        SendNotificationToUser.Definition
      ],
      ReadModels =
      [
        new ReadModelDefinition<ProductStock, Stock>
        {
          Projector = stock =>
          [
            new ProductStock(
              stock.ProductId.ToString(),
              stock.ProductId,
              stock.ProductName,
              stock.Amount,
              stock.PictureId,
              42,
              3.1416f,
              0.1,
              true,
              'a',
              DateTime.SpecifyKind(new DateTime(2017, 1, 1), DateTimeKind.Utc),
              DateTime.SpecifyKind(new DateTime(2017, 1, 1), DateTimeKind.Utc),
              new DateTimeOffset(2017, 1, 1, 1, 1, 1, TimeSpan.Zero),
              new DateTimeOffset(2017, 1, 1, 1, 1, 1, TimeSpan.Zero),
              new TimeOnly(1, 1, 1),
              new TimeOnly(1, 1, 1)
            )
          ],
          StreamPrefix = "entity-stock-",
          AreaTag = "ProductStock"
        },
        new ReadModelDefinition<OrganizationBuildingReadModel, OrganizationBuilding>
        {
          Projector = orgB => [new OrganizationBuildingReadModel(orgB.EntityId, orgB.TenantId, orgB.EntityId)],
          StreamPrefix = OrganizationBuilding.StreamPrefix,
          AreaTag = "OrganizationBuilding"
        },
        new ReadModelDefinition<UserFavoriteFoodReadModel, UserFavoriteFood>
        {
          Projector = uf => [new UserFavoriteFoodReadModel(uf.Sub, uf.FavoriteFood, uf.Sub)],
          StreamPrefix = UserFavoriteFood.StreamPrefix,
          AreaTag = "FavoriteFood"
        },
        new AggregatingReadModelDefinition<AggregatingStockReadModel>
        {
          Aggregators = AggregatingStockReadModel.GetAggregators().ToArray(),
          StreamPrefixes = AggregatingStockReadModel.GetStreamPrefixes().ToArray(),
          AccessoryTables = [typeof(AccessoryStockTable)],
          AreaTag = "ProductStock"
        },
        ProductStoreFrontReadModel.Definition,
        UserRegistryOfNamedProductsReadModel.Definition
      ],
      Projections = [new ProductCreatedToStockNamed(), new ProductPictureUploadedToStockPictureAdded()],
      Tasks = [task],
      Entities =
      [
        new EntityDefinition<Stock, StrongGuid> { Defaulter = Stock.Defaulted, StreamPrefix = Stock.StreamPrefix },
        new EntityDefinition<Product, StrongGuid>
        {
          Defaulter = Product.Defaulted, StreamPrefix = Product.StreamPrefix
        },
        new EntityDefinition<OrganizationBuilding, StrongString>
        {
          Defaulter = OrganizationBuilding.Defaulted, StreamPrefix = OrganizationBuilding.StreamPrefix
        },
        new EntityDefinition<UserFavoriteFood, StrongString>
        {
          Defaulter = UserFavoriteFood.Defaulted, StreamPrefix = UserFavoriteFood.StreamPrefix
        },
        StoreFrontProduct.Definition,
        UserRegistryOfNamedProducts.Definition
      ],
      RecurringTasks =
      [
        new RecurringTaskDefinition
        {
          Interval = TaskInterval.ForAllWeek(
            Enumerable
              .Range(1, 5)
              .Select(i =>
                new TimeOnly(initTime.Hour, initTime.Minute, initTime.Second)
                  .Add(TimeSpan.FromSeconds(i))
              )
              .ToArray()
          ),
          TaskName = "test-stuff-task-a-reno",
          Action = (_, _, _) => Task.FromResult<Du<EventInsertion, TodoOutcome>>(TodoOutcome.Done)
        }
      ],
      Ingestors = [new ProductNameIngestor()],
      StaticEndpoints =
      [
        new StaticEndpointDefinition<MyStaticData>
        {
          GetStaticResponse = user =>
            user
              .Match<MyStaticData>(
                u => new MyStaticData(u.FullName, u.Sub, u.ApplicationPermissions.Contains("admin")),
                () => new MyStaticData("Unknown", null, false)),
          AreaTag = "TestStaticEndpoint"
        }
      ]
    };

    return model
      .Merge(EntityWithFiles.Model())
      .Merge(AsyncPeopleModel.Model)
      .Merge(EntityWithDates.Model)
      .Merge(DefaultedReadModelsModel.Model)
      .Merge(PizzaModel.Model)
      .Merge(GuidValidationModel.Model)
      .Merge(EntityWithEnum.Model)
      .Merge(TenantPermissionsAndRolesModel.Model)
      .Merge(ExternalFoldingModel.Model)
      .Merge(ExtremeCountModel.Model)
      .Merge(MultiTenantModel.Model);
  }

  private class
    ProductCreatedToStockNamed : ProjectionDefinition<ProductCreated, StockNamed, Product, Stock, StrongGuid>
  {
    public override string Name => "projection-product-created-to-stock-named";
    public override string SourcePrefix => Product.StreamPrefix;

    public override Option<StockNamed> Project(
      ProductCreated se,
      Product e,
      Option<Stock> destinationEntity,
      StrongGuid projectionId,
      Uuid eventId,
      EventMetadata metadata) =>
      new StockNamed(e.Id, e.Name);

    public override IEnumerable<StrongGuid> GetProjectionIds(
      ProductCreated sourceEvent,
      Product sourceEntity,
      Uuid sourceEventId) => [new(sourceEvent.ProductId)];
  }

  private class ProductPictureUploadedToStockPictureAdded :
    ProjectionDefinition<ProductPictureAdded, StockPictureAdded, Product, Stock, StrongGuid>
  {
    public override string Name => "projection-product-picture-uploaded-to-stock-picture-added";
    public override string SourcePrefix => Product.StreamPrefix;

    public override Option<StockPictureAdded> Project(
      ProductPictureAdded se,
      Product e,
      Option<Stock> destinationEntity,
      StrongGuid projectionId,
      Uuid eventId,
      EventMetadata metadata) => new StockPictureAdded(e.Id, se.PictureId);

    public override IEnumerable<StrongGuid> GetProjectionIds(
      ProductPictureAdded sourceEvent,
      Product sourceEntity,
      Uuid sourceEventId) => [new(sourceEvent.ProductId)];
  }
}

public record MyStaticData(string Name, string? Sub, bool IsAdmin);
