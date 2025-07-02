using Nvx.ConsistentAPI;

namespace TestEventModel;

// Meant to verify that the cache is working correctly,
// there is no way for the test to pass with the cache not working.
public static class ExtremeCountModel
{
  public static readonly EventModel Model = new()
  {
    Entities =
    [
      new EntityDefinition<ExtremeCountEntity, ExtremeCountId>
      {
        Defaulter = ExtremeCountEntity.Defaulted,
        StreamPrefix = ExtremeCountEntity.StreamPrefix
      }
    ],
    ReadModels =
    [
      new ReadModelDefinition<ExtremeCountReadModel, ExtremeCountEntity>
      {
        StreamPrefix = ExtremeCountEntity.StreamPrefix,
        Projector = entity => [new ExtremeCountReadModel(entity.Id.ToString(), entity.Id, entity.Count)],
        AreaTag = "ExtremeCount"
      }
    ],
    Commands =
    [
      new CommandDefinition<MakeItCount, ExtremeCountEntity>
      {
        AreaTag = "ExtremeCount"
      }
    ]
  };
}

public record ExtremeCountId(Guid Id) : StrongId
{
  public override string StreamId() =>
    $"{ExtremeCountEntity.StreamPrefix}{Id}";

  public override string ToString() => StreamId();
}

public record ExtremeCountHappened(Guid Id) : EventModelEvent
{
  public string GetStreamName() => ExtremeCountEntity.GetStreamName(new ExtremeCountId(Id));
  public StrongId GetEntityId() => new ExtremeCountId(Id);
}

public partial record ExtremeCountEntity(Guid Id, int Count) : EventModelEntity<ExtremeCountEntity>,
  Folds<ExtremeCountHappened, ExtremeCountEntity>
{
  public const string StreamPrefix = "extreme-count-entity-";
  public string GetStreamName() => GetStreamName(new ExtremeCountId(Id));

  public ValueTask<ExtremeCountEntity> Fold(
    ExtremeCountHappened evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this with { Count = Count + 1 });

  public static string GetStreamName(ExtremeCountId id) => $"{StreamPrefix}{id}";

  public static ExtremeCountEntity Defaulted(ExtremeCountId id) => new(id.Id, 0);
}

public record ExtremeCountReadModel(string Id, Guid EntityId, int Count) : EventModelReadModel
{
  public StrongId GetStrongId() => new ExtremeCountId(EntityId);
}

public record MakeItCount(Guid Id) : EventModelCommand<ExtremeCountEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<ExtremeCountEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) => new AnyState(new ExtremeCountHappened(Id));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new ExtremeCountId(Id);
}
