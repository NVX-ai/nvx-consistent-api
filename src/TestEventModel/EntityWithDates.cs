using Nvx.ConsistentAPI;

namespace TestEventModel;

public partial record EntityWithDates(Guid Id, DateTimeOffset TheDate, DateOnly OnlyTheDate) :
  EventModelEntity<EntityWithDates>,
  Folds<EntityWithDatesSaved, EntityWithDates>
{
  public const string StreamPrefix = "entity-with-dates-";

  public static readonly EventModel Model = new()
  {
    Entities =
      [new EntityDefinition<EntityWithDates, StrongGuid> { Defaulter = Defaulted, StreamPrefix = StreamPrefix }],
    Commands = [new CommandDefinition<SaveEntityWithDates, EntityWithDates> { AreaTag = "TestDates" }],
    ReadModels =
    [
      new ReadModelDefinition<EntityWithDatesReadModel, EntityWithDates>
      {
        StreamPrefix = StreamPrefix, Projector = EntityWithDatesReadModel.From, AreaTag = "TestDates"
      }
    ]
  };

  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<EntityWithDates> Fold(
    EntityWithDatesSaved evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { TheDate = evt.TheDate, OnlyTheDate = evt.TheDate.ToLocalDateOnly() });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";
  public static EntityWithDates Defaulted(StrongGuid id) => new(id.Value, DateTimeOffset.MinValue, DateOnly.MinValue);
}

public record SaveEntityWithDates(Guid EntityId, DateTimeOffset TheDate) : EventModelCommand<EntityWithDates>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<EntityWithDates> entity,
    Option<UserSecurity> user,
    FileUpload[] files) => new AnyState(new EntityWithDatesSaved(EntityId, TheDate));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(EntityId);
}

public record EntityWithDatesSaved(Guid EntityId, DateTimeOffset TheDate) : EventModelEvent
{
  public string GetStreamName() => EntityWithDates.GetStreamName(EntityId);
  public StrongId GetEntityId() => new StrongGuid(EntityId);
}

// LongAgo is here to trigger the inability of MSSQL to store dates bellow the year 1753
public record EntityWithDatesReadModel(string Id, DateTimeOffset TheDate, DateTime TheDateTime, DateTime LongAgo, DateOnly OnlyTheDate)
  : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongString(Id);

  public static EntityWithDatesReadModel[] From(EntityWithDates entity) =>
    [new(entity.Id.ToString(), entity.TheDate, entity.TheDate.DateTime, DateTime.MinValue, entity.OnlyTheDate)];
}
