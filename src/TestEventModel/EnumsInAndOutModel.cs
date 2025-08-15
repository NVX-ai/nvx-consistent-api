using Nvx.ConsistentAPI;

namespace TestEventModel;

public partial record EntityWithEnum(Guid Id, OneEnum EnumValue)
  : EventModelEntity<EntityWithEnum>, Folds<EntityWithEnumSaved, EntityWithEnum>
{
  internal const string StreamPrefix = "entity-with-enum-";

  public static readonly EventModel Model = new()
  {
    Entities =
      [new EntityDefinition<EntityWithEnum, StrongGuid> { Defaulter = Defaulted, StreamPrefix = StreamPrefix }],
    Commands = [new CommandDefinition<SaveEntityWithEnum, EntityWithEnum> { AreaTag = "TestEnums" }],
    ReadModels =
    [
      new ReadModelDefinition<EntityWithEnumReadModel, EntityWithEnum>
        { StreamPrefix = StreamPrefix, Projector = EntityWithEnumReadModel.From, AreaTag = "TestEnums" }
    ]
  };

  public string GetStreamName() => GetStreamName(new StrongGuid(Id));

  public ValueTask<EntityWithEnum> Fold(EntityWithEnumSaved evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { EnumValue = evt.EnumValue });

  public static string GetStreamName(StrongGuid id) => $"{StreamPrefix}{id.Value}";

  public static EntityWithEnum Defaulted(StrongGuid id) => new(id.Value, OneEnum.Word);
}

public record SaveEntityWithEnum(OneEnum EnumValue) : EventModelCommand<EntityWithEnum>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<EntityWithEnum> entity,
    Option<UserSecurity> user,
    FileUpload[] files) =>
    new AnyState(new EntityWithEnumSaved(Guid.NewGuid(), EnumValue));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => None;
}

public record EntityWithEnumSaved(Guid EntityId, OneEnum EnumValue) : EventModelEvent
{
  public string GetSwimLane() => EntityWithEnum.StreamPrefix;
  public StrongId GetEntityId() => new StrongGuid(EntityId);
}

public record EntityWithEnumReadModel(string Id, Guid EntityId, OneEnum EnumValue) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongGuid(EntityId);

  public static EntityWithEnumReadModel[] From(EntityWithEnum entity) =>
    [new(entity.Id.ToString(), entity.Id, entity.EnumValue)];
}

public enum OneEnum
{
  Word = 0,
  MultipleWords = 1,
  EvenMoreWords = 2
}
