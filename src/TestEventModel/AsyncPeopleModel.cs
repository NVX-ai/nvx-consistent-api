using Nvx.ConsistentAPI;

namespace TestEventModel;

public static class AsyncPeopleModel
{
  public static readonly EventModel Model = new()
  {
    Entities =
    [
      new EntityDefinition<PersonEntity, PersonId>
      {
        Defaulter = PersonEntity.Defaulted, StreamPrefix = PersonEntity.StreamPrefix
      }
    ],
    ReadModels =
    [
      new ReadModelDefinition<PersonReadModel, PersonEntity>
      {
        StreamPrefix = PersonEntity.StreamPrefix,
        Projector = PersonReadModel.FromEntity,
        AreaTag = "AsynchronousPeople"
      }
    ],
    Commands =
    [
      new CommandDefinition<CreatePerson, PersonEntity> { AreaTag = "AsynchronousPeople" },
      new CommandDefinition<AddChild, PersonEntity> { AreaTag = "AsynchronousPeople" },
      new CommandDefinition<RenamePerson, PersonEntity> { AreaTag = "AsynchronousPeople" }
    ]
  };
}

public record PersonId(Guid Value) : StrongId
{
  public override string StreamId() => Value.ToString();
  public override string ToString() => Value.ToString();
}

public partial record PersonEntity(Guid Id, string Name, PersonEntity[] Children) : EventModelEntity<PersonEntity>,
  Folds<PersonCreated, PersonEntity>,
  Folds<PersonChildAdded, PersonEntity>,
  Folds<PersonNameUpdated, PersonEntity>
{
  public const string StreamPrefix = "test-async-person-entity-";
  public string GetStreamName() => GetStreamName(new PersonId(Id));

  public async ValueTask<PersonEntity> Fold(
    PersonChildAdded evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await fetcher
      .Fetch<PersonEntity>(new PersonId(evt.ChildId))
      .Match(
        p => this with { Children = Children.Where(c => c.Id != p.Id).Append(p).ToArray() },
        () => this);

  public ValueTask<PersonEntity> Fold(PersonCreated evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Name = evt.Name });

  public ValueTask<PersonEntity> Fold(PersonNameUpdated evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Name = evt.Name });

  public static string GetStreamName(PersonId id) => $"{StreamPrefix}{id.Value}";
  public static PersonEntity Defaulted(PersonId id) => new(id.Value, string.Empty, []);
}

public record PersonCreated(Guid Id, string Name) : EventModelEvent
{
  public string GetSwimLane() => PersonEntity.StreamPrefix;
  public StrongId GetEntityId() => new PersonId(Id);
}

public record PersonChildAdded(Guid ParentId, Guid ChildId) : EventModelEvent
{
  public string GetSwimLane() => PersonEntity.StreamPrefix;
  public StrongId GetEntityId() => new PersonId(ParentId);
}

public record PersonNameUpdated(Guid Id, string Name) : EventModelEvent
{
  public string GetSwimLane() => PersonEntity.StreamPrefix;
  public StrongId GetEntityId() => new PersonId(Id);
}

public record PersonReadModel(string Id, Guid PersonId, string Name, PersonReadModel[] Children) : EventModelReadModel
{
  public StrongId GetStrongId() => new PersonId(PersonId);

  public static PersonReadModel[] FromEntity(PersonEntity entity) =>
  [
    new(
      entity.Id.ToString(),
      entity.Id,
      entity.Name,
      entity.Children.SelectMany(FromEntity).ToArray())
  ];
}

public record CreatePerson(string Name) : EventModelCommand<PersonEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<PersonEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) => this.ShouldCreate(entity, new PersonCreated(Guid.NewGuid(), Name));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => None;
}

public record AddChild(Guid ParentId, Guid ChildId) : EventModelCommand<PersonEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<PersonEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) => this.Require(entity, user, new PersonChildAdded(ParentId, ChildId));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new PersonId(ParentId);
}

public record RenamePerson(Guid Id, string Name) : EventModelCommand<PersonEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<PersonEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files) => this.Require(entity, user, new PersonNameUpdated(Id, Name));

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new PersonId(Id);
}
