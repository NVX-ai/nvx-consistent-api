using Nvx.ConsistentAPI;

namespace TestEventModel;

public static class GuidValidationModel
{
  public static readonly EventModel Model = new()
  {
    Entities =
    [
      new EntityDefinition<GuidValidationEntity, GuidValidationId>
      {
        Defaulter = GuidValidationEntity.Defaulted,
        StreamPrefix = GuidValidationEntity.StreamPrefix
      }
    ],
    Commands =
    [
      new CommandDefinition<GuidValidationCommand, GuidValidationEntity>
      {
        AreaTag = "GuidValidation"
      }
    ]
  };


  public record GuidValidationCommand(Guid Id, Guid? NullableId) : EventModelCommand<GuidValidationEntity>
  {
    public Result<EventInsertion, ApiError> Decide(
      Option<GuidValidationEntity> entity,
      Option<UserSecurity> user,
      FileUpload[] files) => new AnyState(new GuidValidationEvent(Id));

    public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new GuidValidationId(Id);
  }
}

public record GuidValidationEvent(Guid Id) : EventModelEvent
{
  public string SwimLane => GuidValidationEntity.StreamPrefix;
  public StrongId GetEntityId() => new GuidValidationId(Id);
}

public partial record GuidValidationEntity(Guid Id)
  : EventModelEntity<GuidValidationEntity>,
    Folds<GuidValidationEvent, GuidValidationEntity>
{
  public const string StreamPrefix = "entity-guid-validation-";
  public string GetStreamName() => GetStreamName(new GuidValidationId(Id));

  public ValueTask<GuidValidationEntity> Fold(
    GuidValidationEvent evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this);

  public static GuidValidationEntity Defaulted(GuidValidationId id) => new(id.Id);
  public static string GetStreamName(GuidValidationId id) => $"{StreamPrefix}{id}";
}

public record GuidValidationId(Guid Id) : StrongId
{
  public override string StreamId() => Id.ToString();
  public override string ToString() => StreamId();
}
