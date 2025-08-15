using Nvx.ConsistentAPI;

namespace TestEventModel;

public partial record EntityWithFiles(string Id, MyFile[] Files)
  : EventModelEntity<EntityWithFiles>, Folds<SavedEntityWithFiles, EntityWithFiles>
{
  public const string StreamPrefix = "entity-with-files-";
  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<EntityWithFiles> Fold(
    SavedEntityWithFiles evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Files = evt.Files });

  public static string GetStreamName(string id) => $"{StreamPrefix}{id}";
  public static EntityWithFiles Defaulted(StrongString id) => new(id.Value, []);

  public static EventModel Model() => new()
  {
    Entities =
    [
      new EntityDefinition<EntityWithFiles, StrongString> { Defaulter = Defaulted, StreamPrefix = StreamPrefix }
    ],
    Commands =
    [
      new CommandDefinition<SaveEntityWithFiles, EntityWithFiles> { AreaTag = "TestFiles" }
    ],
    ReadModels =
    [
      new ReadModelDefinition<EntityWithFilesReadModel, EntityWithFiles>
      {
        StreamPrefix = StreamPrefix, Projector = EntityWithFilesReadModel.From, AreaTag = "TestFiles"
      }
    ]
  };
}

public record SavedEntityWithFiles(string Id, MyFile[] Files) : EventModelEvent
{
  public string GetSwimLane() => EntityWithFiles.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Id);
}

public record SaveEntityWithFiles(string Id, AttachedFile[] Files) : EventModelCommand<EntityWithFiles>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<EntityWithFiles> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) => new AnyState(
    new SavedEntityWithFiles(
      Id,
      Files
        .Select(f => new MyFile(f.Id, files.First(fu => fu.Id == f.Id).FileName, files.First(fu => fu.Id == f.Id).Tags))
        .ToArray())
  );

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
}

public record MyFile(Guid FileId, string FileName, string[] Tags);

public record EntityWithFilesReadModel(string Id, MyFile[] Files, string[] FileNames) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongString(Id);

  public static EntityWithFilesReadModel[] From(EntityWithFiles entity) =>
    [new(entity.Id, entity.Files, entity.Files.Select(f => f.FileName).ToArray())];
}
