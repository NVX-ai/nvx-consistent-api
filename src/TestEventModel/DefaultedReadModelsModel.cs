using Nvx.ConsistentAPI;

namespace TestEventModel;

public static class DefaultedReadModelsModel
{
  public static readonly EventModel Model = new()
  {
    Entities =
    [
      new EntityDefinition<DefaultedReadModelEntity, DefaultedReadModelId>
      {
        Defaulter = DefaultedReadModelEntity.Defaulted,
        StreamPrefix = DefaultedReadModelEntity.StreamPrefix
      }
    ],
    ReadModels =
    [
      new ReadModelDefinition<DefaultedReadModelReadModel, DefaultedReadModelEntity>
      {
        StreamPrefix = DefaultedReadModelEntity.StreamPrefix,
        Projector = DefaultedReadModelReadModel.From,
        AreaTag = "DefaultedReadModel",
        Defaulter = (id, _, _) =>
          Guid.TryParse(id, out var parsed)
            ? new DefaultedReadModelReadModel(id, parsed, "This was defaulted")
            : None
      }
    ],
    Commands =
    [
      new CommandDefinition<DoSomethingToDefaultedReadModel, DefaultedReadModelEntity>
      {
        AreaTag = "DefaultedReadModel"
      }
    ]
  };
}

public record DefaultedReadModelId(Guid Id) : StrongId
{
  public override string StreamId() => Id.ToString();
  public override string ToString() => StreamId();
}

public partial record DefaultedReadModelEntity(Guid Id) : EventModelEntity<DefaultedReadModelEntity>,
  Folds<SomethingHappenedToDefaultedReadModel, DefaultedReadModelEntity>
{
  public const string StreamPrefix = "entity-defaulted-read-model-";
  public string GetStreamName() => GetStreamName(new DefaultedReadModelId(Id));

  public ValueTask<DefaultedReadModelEntity> Fold(
    SomethingHappenedToDefaultedReadModel evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this);

  public static DefaultedReadModelEntity Defaulted(DefaultedReadModelId id) => new(id.Id);
  public static string GetStreamName(DefaultedReadModelId id) => $"{StreamPrefix}{id}";
}

public record DefaultedReadModelReadModel(string Id, Guid EntityId, string SomeText) : EventModelReadModel
{
  public StrongId GetStrongId() => new DefaultedReadModelId(EntityId);

  public static DefaultedReadModelReadModel[] From(DefaultedReadModelEntity entity) =>
    [new(entity.Id.ToString(), entity.Id, "This is not default")];
}

public record SomethingHappenedToDefaultedReadModel(Guid Id) : EventModelEvent
{
  public string GetSwimLane() => DefaultedReadModelEntity.StreamPrefix;
  public StrongId GetEntityId() => new DefaultedReadModelId(Id);
}

public record DoSomethingToDefaultedReadModel(Guid Id) : EventModelCommand<DefaultedReadModelEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<DefaultedReadModelEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) => new AnyState(new SomethingHappenedToDefaultedReadModel(Id));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new DefaultedReadModelId(Id);
}
