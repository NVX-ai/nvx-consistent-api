using System.Data.Common;
using Dapper;
using Nvx.ConsistentAPI;

namespace TestEventModel;

public static class PizzaModel
{
  public static readonly EventModel Model = new()
  {
    Entities =
    [
      new EntityDefinition<PizzaEntity, StrongGuid>
      {
        Defaulter = PizzaEntity.Defaulted, StreamPrefix = PizzaEntity.StreamPrefix
      },
      new EntityDefinition<ExternalPizzaEntity, StrongGuid>
      {
        Defaulter = ExternalPizzaEntity.Defaulted, StreamPrefix = ExternalPizzaEntity.StreamPrefix
      },
      new EntityDefinition<IngredientEntity, StrongGuid>
      {
        Defaulter = IngredientEntity.Defaulted, StreamPrefix = IngredientEntity.StreamPrefix
      }
    ],
    Commands =
    [
      new CommandDefinition<CreatePizza, PizzaEntity> { AreaTag = "Pizza" },
      new CommandDefinition<CreateExternalPizza, ExternalPizzaEntity> { AreaTag = "Pizza" },
      new CommandDefinition<CreateIngredient, IngredientEntity> { AreaTag = "Pizza" },
      new CommandDefinition<SupplyIngredient, IngredientEntity> { AreaTag = "Pizza" }
    ],
    ReadModels =
    [
      new ReadModelDefinition<AvailablePizzaReadModel, PizzaEntity>
      {
        StreamPrefix = PizzaEntity.StreamPrefix,
        Projector = AvailablePizzaReadModel.From,
        AreaTag = "Pizza",
        CustomFilterBuilder = AvailablePizzaReadModel.CustomFilterBuilder
      },
      new ReadModelDefinition<ExternalPizzaReadModel, ExternalPizzaEntity>
      {
        StreamPrefix = ExternalPizzaEntity.StreamPrefix,
        Projector = ExternalPizzaReadModel.From,
        AreaTag = "Pizza",
        CustomFilterBuilder = ExternalPizzaReadModel.CustomFilterBuilder
      },
      PizzaStockReadModel.Definition
    ]
  };
}

public partial record PizzaEntity(Guid Id, Guid[] IngredientIds, Guid TenantId)
  : EventModelEntity<PizzaEntity>, Folds<PizzaCreated, PizzaEntity>
{
  public const string StreamPrefix = "pizza-entity-";
  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<PizzaEntity> Fold(PizzaCreated evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { IngredientIds = evt.IngredientIds, TenantId = evt.TenantId });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";
  public static PizzaEntity Defaulted(StrongGuid id) => new(id.Value, [], new Guid());
}

public partial record ExternalPizzaEntity(Guid Id, Guid PizzaId, Guid TenantId)
  : EventModelEntity<ExternalPizzaEntity>, Folds<ExternalPizzaCreated, ExternalPizzaEntity>
{
  public const string StreamPrefix = "external-pizza-entity-";
  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<ExternalPizzaEntity> Fold(
    ExternalPizzaCreated evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { TenantId = evt.TenantId, PizzaId = evt.PizzaId });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";
  public static ExternalPizzaEntity Defaulted(StrongGuid id) => new(id.Value, Guid.Empty, Guid.Empty);
}

public record PizzaCreated(Guid Id, Guid[] IngredientIds, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => PizzaEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record ExternalPizzaCreated(Guid Id, Guid PizzaId, Guid TenantId) : EventModelEvent
{
  public string GetStreamName() => ExternalPizzaEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record CreatePizza(Guid[] IngredientIds) : TenantEventModelCommand<PizzaEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<PizzaEntity> entity,
    UserSecurity user,
    FileUpload[] files) =>
    this.ShouldCreate(
      entity,
      () => new EventModelEvent[] { new PizzaCreated(Guid.NewGuid(), IngredientIds, tenantId) });

  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => None;
}

public record CreateExternalPizza(Guid PizzaId) : TenantEventModelCommand<ExternalPizzaEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<ExternalPizzaEntity> entity,
    UserSecurity user,
    FileUpload[] files) =>
    this.ShouldCreate(
      entity,
      () => new EventModelEvent[] { new ExternalPizzaCreated(Guid.NewGuid(), PizzaId, tenantId) });

  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => None;
}

public record AvailablePizzaReadModel(string Id, Guid TenantId) : EventModelReadModel, IsTenantBound
{
  public StrongId GetStrongId() => new StrongString(Id);

  public static AvailablePizzaReadModel[] From(PizzaEntity entity) =>
    [new(entity.Id.ToString(), entity.TenantId)];

  public static CustomFilter CustomFilterBuilder(
    Option<UserSecurity> user,
    Option<Guid> tenantId,
    ReadModelDetailsFactory factory)
  {
    var pizzaTableDetails = factory.GetTableDetails<AvailablePizzaReadModel>();
    var pizzaStockTableDetails = factory.GetTableDetails<PizzaStockReadModel>();
    var joinClause =
      $"""
       LEFT JOIN [{pizzaStockTableDetails.TableName}] as [pizzaStock]
       ON [{pizzaTableDetails.TableName}].[Id] = [pizzaStock].[Id]
       """;

    const string whereClause = "[pizzaStock].[Stock] > 0";
    const string additionalColumns = "[pizzaStock].[Stock]";

    return new CustomFilter(joinClause, [whereClause], additionalColumns);
  }
}

public record ExternalPizzaReadModel(string Id, Guid PizzaId, Guid TenantId) : EventModelReadModel, IsTenantBound
{
  public StrongId GetStrongId() => new StrongString(Id);

  public static ExternalPizzaReadModel[] From(ExternalPizzaEntity entity) =>
    [new(entity.Id.ToString(), entity.PizzaId, entity.TenantId)];

  public static CustomFilter CustomFilterBuilder(
    Option<UserSecurity> user,
    Option<Guid> tenantId,
    ReadModelDetailsFactory factory)
  {
    var externalPizzaTableDetails = factory.GetTableDetails<ExternalPizzaReadModel>();
    var pizzaTableDetails = factory.GetTableDetails<AvailablePizzaReadModel>();
    var joinClause =
      $"""
       LEFT JOIN [{pizzaTableDetails.TableName}] as [availablePizza]
       ON [{externalPizzaTableDetails.TableName}].[PizzaId] = [availablePizza].[Id] AND [availablePizza].[TenantId] = [{externalPizzaTableDetails.TableName}].[TenantId]
       """;

    var whereClause =
      $"[{externalPizzaTableDetails.TableName}].[TenantId] = @tenantId OR [availablePizza].[TenantId] = @tenantId";

    return new CustomFilter(joinClause, [whereClause], "", true);
  }
}

public partial record IngredientEntity(Guid Id, int Stock)
  : EventModelEntity<IngredientEntity>,
    Folds<IngredientCreated, IngredientEntity>,
    Folds<IngredientStockReceived, IngredientEntity>
{
  public const string StreamPrefix = "ingredient-entity-";
  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<IngredientEntity> Fold(
    IngredientCreated evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this);

  public ValueTask<IngredientEntity> Fold(
    IngredientStockReceived evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Stock = Stock + evt.Amount });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";
  public static IngredientEntity Defaulted(StrongGuid id) => new(id.Value, 0);
}

public record IngredientCreated(Guid Id) : EventModelEvent
{
  public string GetStreamName() => IngredientEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record IngredientStockReceived(Guid Id, int Amount) : EventModelEvent
{
  public string GetStreamName() => IngredientEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record CreateIngredient : EventModelCommand<IngredientEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<IngredientEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) =>
    this.ShouldCreate(entity, () => new EventModelEvent[] { new IngredientCreated(Guid.NewGuid()) });

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => None;
}

public record SupplyIngredient(Guid IngredientId, int Amount) : EventModelCommand<IngredientEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<IngredientEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) =>
    this.Require(entity, _ => new ExistingStream(new IngredientStockReceived(IngredientId, Amount)));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(IngredientId);
}

public record PizzaStockReadModel(string Id, Guid PizzaId, int Stock) : EventModelReadModel
{
  public static readonly EventModelingReadModelArtifact Definition =
    new AggregatingReadModelDefinition<PizzaStockReadModel>
    {
      Aggregators = [new PizzaCreatedAggregator(), new IngredientStockReceivedAggregator()],
      StreamPrefixes = [PizzaEntity.StreamPrefix, IngredientEntity.StreamPrefix],
      IsExposed = false,
      AccessoryTables = [typeof(PizzaIngredientRelation)],
      AreaTag = "Pizza"
    };

  public StrongId GetStrongId() => new StrongString(Id);

  private static async Task<string[]> UpdateStockForPizza(
    Guid pizzaId,
    Fetcher fetcher,
    DbConnection dbConnection,
    DbTransaction dbTransaction,
    TableDetails tableDetails) =>
    await fetcher
      .Fetch<PizzaEntity>(new StrongGuid(pizzaId))
      .Map(e => e.Ent)
      .Async()
      .Map<string[]>(async pizza =>
      {
        var ingredientRelations =
          pizza.IngredientIds.Select(i => new PizzaIngredientRelation($"{pizzaId}{i}", pizzaId, i));
        var relationDetails = tableDetails.AdditionalTables[typeof(PizzaIngredientRelation)];
        foreach (var ingredientRelation in ingredientRelations)
        {
          await dbConnection.ExecuteAsync(relationDetails.UpsertSql, ingredientRelation, dbTransaction);
        }

        var ingredients = await pizza
          .IngredientIds.Select<Guid, Func<Task<Option<IngredientEntity>>>>(ingId => async () =>
            await fetcher.Fetch<IngredientEntity>(new StrongGuid(ingId)).Map(e => e.Ent))
          .Parallel()
          .Map(ingredientOptions => ingredientOptions.Choose(Prelude.Id).ToArray());

        var stock = ingredients.Length == 0 || pizza.IngredientIds.Any(id => ingredients.All(i => i.Id != id))
          ? 0
          : ingredients.Min(i => i.Stock);

        await dbConnection.ExecuteAsync(
          tableDetails.UpsertSql,
          new PizzaStockReadModel(pizzaId.ToString(), pizzaId, stock),
          dbTransaction);

        return [];
      })
      .DefaultValue([]);

  private static async Task<string[]> UpdateStockForIngredient(
    Guid ingredientId,
    Fetcher fetcher,
    DbConnection dbConnection,
    DbTransaction dbTransaction,
    TableDetails tableDetails) => await fetcher
    .Fetch<IngredientEntity>(new StrongGuid(ingredientId))
    .Map(e => e.Ent)
    .Async()
    .Map<string[]>(async _ =>
    {
      var relationDetails = tableDetails.AdditionalTables[typeof(PizzaIngredientRelation)];
      var relations = await dbConnection
        .QueryAsync<PizzaIngredientRelation>(
          $"SELECT {relationDetails.AllColumns} FROM {relationDetails.TableName} WHERE IngredientId = @ingredientId",
          new { ingredientId },
          dbTransaction)
        .Map(r => r.ToArray());

      if (relations.Length == 0)
      {
        return [];
      }

      var idParams = string.Join(", ", relations.Select((_, i) => $"@id{i}"));

      var parameters = new DynamicParameters();

      foreach (var tuple in relations.Select((r, i) => (r.PizzaId, i)))
      {
        parameters.Add($"@id{tuple.i}", tuple.PizzaId);
      }

      var pizzaStocks = await dbConnection.QueryAsync<PizzaStockReadModel>(
        $"SELECT {tableDetails.AllColumns} FROM [{tableDetails.TableName}] WHERE [Id] IN ({idParams})",
        parameters,
        dbTransaction);
      foreach (var pizzaStock in pizzaStocks)
      {
        await UpdateStockForPizza(pizzaStock.PizzaId, fetcher, dbConnection, dbTransaction, tableDetails);
      }

      return [];
    })
    .DefaultValue([]);

  private class PizzaCreatedAggregator : ReadModelAggregator<PizzaCreated>
  {
    protected override async Task<string[]> Aggregate(
      EventWithMetadata<PizzaCreated> evt,
      Fetcher fetcher,
      DbConnection dbConnection,
      DbTransaction dbTransaction,
      TableDetails tableDetails) => await UpdateStockForPizza(
      evt.Event.Id,
      fetcher,
      dbConnection,
      dbTransaction,
      tableDetails);
  }

  private class IngredientStockReceivedAggregator : ReadModelAggregator<IngredientStockReceived>
  {
    protected override async Task<string[]> Aggregate(
      EventWithMetadata<IngredientStockReceived> evt,
      Fetcher fetcher,
      DbConnection dbConnection,
      DbTransaction dbTransaction,
      TableDetails tableDetails) => await UpdateStockForIngredient(
      evt.Event.Id,
      fetcher,
      dbConnection,
      dbTransaction,
      tableDetails);
  }
}

public record PizzaIngredientRelation(string Id, Guid PizzaId, Guid IngredientId) : HasId;
