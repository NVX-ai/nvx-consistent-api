using Nvx.ConsistentAPI;

namespace TestEventModel;

public static class ExternalFoldingModel
{
  public static readonly EventModel Model = new()
  {
    Entities =
    [
      EntityThatIsInterested.Definition,
      FirstDegreeConcernedEntity.Definition,
      SecondDegreeConcernedEntity.Definition
    ],
    ReadModels =
    [
      EntityThatDependsReadModel.Definition
    ],
    InterestTriggers =
    [
      new InitiatesInterest<InterestedEntityAddedAnInterest>(evt =>
      [
        new EntityInterestManifest(
          evt.GetStreamName(),
          evt.GetEntityId(),
          FirstDegreeConcernedEntity.GetStreamName(evt.DependsOnId),
          new StrongGuid(evt.DependsOnId))
      ]),
      new StopsInterest<InterestedEntityRemovedInterest>(evt =>
      [
        new EntityInterestManifest(
          evt.GetStreamName(),
          evt.GetEntityId(),
          FirstDegreeConcernedEntity.GetStreamName(evt.DependsOnId),
          new StrongGuid(evt.DependsOnId))
      ]),
      new InitiatesInterest<FirstDegreeConcernedEntityEventAboutInterestedEntity>(evt =>
      [
        new EntityInterestManifest(
          EntityThatIsInterested.GetStreamName(evt.EntityThatDependsId),
          new StrongGuid(evt.EntityThatDependsId),
          evt.GetStreamName(),
          evt.GetEntityId())
      ]),
      new InitiatesInterest<FirstDegreeStartedDependingOnSecondDegree>(evt =>
      [
        new EntityInterestManifest(
          FirstDegreeConcernedEntity.GetStreamName(evt.Id),
          new StrongGuid(evt.Id),
          SecondDegreeConcernedEntity.GetStreamName(evt.SecondDegreeId),
          new StrongGuid(evt.SecondDegreeId))
      ])
    ]
  };
}

public record EntityThatDependsReadModel(
  string Id,
  Guid EntityId,
  Guid[] DependsOnIds,
  string[] DependedOnTags,
  string[] FarAwayNames)
  : EventModelReadModel
{
  public static readonly EventModelingReadModelArtifact Definition =
    new ReadModelDefinition<EntityThatDependsReadModel, EntityThatIsInterested>
    {
      StreamPrefix = EntityThatIsInterested.StreamPrefix,
      Projector = From,
      AreaTag = "TestExternalFolding"
    };

  public StrongId GetStrongId() => new StrongGuid(EntityId);

  public static EntityThatDependsReadModel[] From(EntityThatIsInterested entity) =>
    [new(entity.Id.ToString(), entity.Id, entity.ConcernedIds, entity.ConcernedTags, entity.SecondDegreeNames)];
}

public record InterestedEntityAddedAnInterest(Guid Id, Guid DependsOnId) : EventModelEvent
{
  public string GetStreamName() => EntityThatIsInterested.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record InterestedEntityRemovedInterest(Guid Id, Guid DependsOnId) : EventModelEvent
{
  public string GetStreamName() => EntityThatIsInterested.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record SecondDegreeConcernedEntityNamed(Guid Id, string Name) : EventModelEvent
{
  public string GetStreamName() => SecondDegreeConcernedEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record FirstDegreeStartedDependingOnSecondDegree(Guid Id, Guid SecondDegreeId) : EventModelEvent
{
  public string GetStreamName() => FirstDegreeConcernedEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public partial record EntityThatIsInterested(
  Guid Id,
  Guid[] ConcernedIds,
  string[] ConcernedTags,
  string[] SecondDegreeNames)
  : EventModelEntity<EntityThatIsInterested>,
    Folds<InterestedEntityAddedAnInterest, EntityThatIsInterested>,
    Folds<InterestedEntityRemovedInterest, EntityThatIsInterested>,
    FoldsExternally<FirstDegreeConcernedEntityTagged, EntityThatIsInterested>,
    FoldsExternally<FirstDegreeConcernedEntityEventAboutInterestedEntity, EntityThatIsInterested>,
    FoldsExternally<SecondDegreeConcernedEntityNamed, EntityThatIsInterested>
{
  public const string StreamPrefix = "entity-that-is-interested-entity-";

  public static readonly EntityDefinition Definition =
    new EntityDefinition<EntityThatIsInterested, StrongGuid>
    {
      Defaulter = Defaulted,
      StreamPrefix = StreamPrefix
    };

  public string GetStreamName() => GetStreamName(Id);

  public async ValueTask<EntityThatIsInterested> Fold(
    InterestedEntityAddedAnInterest evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await ConcernedIds
      .Append(evt.DependsOnId)
      .Distinct()
      .ToArray()
      .Apply(async ids =>
        this with
        {
          ConcernedIds = ids,
          ConcernedTags = await GetTags(fetcher, ids)
        });

  public async ValueTask<EntityThatIsInterested> Fold(
    InterestedEntityRemovedInterest evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await ConcernedIds
      .Where(s => s != evt.DependsOnId)
      .Distinct()
      .ToArray()
      .Apply(async ids =>
        this with
        {
          ConcernedIds = ids,
          ConcernedTags = await GetTags(fetcher, ids)
        });

  public async ValueTask<EntityThatIsInterested> Fold(
    FirstDegreeConcernedEntityEventAboutInterestedEntity evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    this with
    {
      ConcernedIds = ConcernedIds.Append(evt.Id).Distinct().ToArray(),
      SecondDegreeNames = SecondDegreeNames
        .Concat(
          await fetcher
            .LatestFetch<FirstDegreeConcernedEntity>(new StrongGuid(evt.Id))
            .Map(e => e.SecondDegreeNames)
            .DefaultValue([]))
        .Distinct()
        .ToArray()
    };

  public async ValueTask<EntityThatIsInterested> Fold(
    FirstDegreeConcernedEntityTagged evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    this with { ConcernedTags = await GetTags(fetcher, ConcernedIds) };

  public async ValueTask<EntityThatIsInterested> Fold(
    SecondDegreeConcernedEntityNamed evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await fetcher
      .LatestFetch<SecondDegreeConcernedEntity>(new StrongGuid(evt.Id))
      .Map(e => e.Name)
      .Match(n => this with { SecondDegreeNames = SecondDegreeNames.Append(n).Distinct().ToArray() }, () => this);

  private static async Task<string[]> GetTags(RevisionFetcher fetcher, Guid[] ids) =>
    await ids
      .Select<Guid, Func<Task<string[]>>>(id => async () =>
        await fetcher
          .LatestFetch<FirstDegreeConcernedEntity>(new StrongGuid(id))
          .Match(
            edo => edo.Tags,
            () => []
          ))
      .Parallel()
      .Map(nestedIds => nestedIds.SelectMany(Prelude.Id).ToArray());

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";
  public static EntityThatIsInterested Defaulted(StrongGuid id) => new(id.Value, [], [], []);
}

partial record SecondDegreeConcernedEntity(Guid Id, string Name)
  : EventModelEntity<SecondDegreeConcernedEntity>,
    Folds<SecondDegreeConcernedEntityNamed, SecondDegreeConcernedEntity>
{
  public const string StreamPrefix = "second-degree-concerned-entity-";

  public static readonly EntityDefinition Definition =
    new EntityDefinition<SecondDegreeConcernedEntity, StrongGuid>
    {
      Defaulter = Defaulted,
      StreamPrefix = StreamPrefix
    };

  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<SecondDegreeConcernedEntity> Fold(
    SecondDegreeConcernedEntityNamed evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this with { Name = evt.Name });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";

  public static SecondDegreeConcernedEntity Defaulted(StrongGuid id) => new(id.Value, string.Empty);
}

public partial record FirstDegreeConcernedEntity(Guid Id, string[] Tags, string[] SecondDegreeNames)
  : EventModelEntity<FirstDegreeConcernedEntity>,
    Folds<FirstDegreeConcernedEntityTagged, FirstDegreeConcernedEntity>,
    Folds<FirstDegreeConcernedEntityEventAboutInterestedEntity, FirstDegreeConcernedEntity>,
    Folds<FirstDegreeStartedDependingOnSecondDegree, FirstDegreeConcernedEntity>,
    FoldsExternally<SecondDegreeConcernedEntityNamed, FirstDegreeConcernedEntity>
{
  public const string StreamPrefix = "first-degree-concerned-entity-";

  public static readonly EntityDefinition Definition =
    new EntityDefinition<FirstDegreeConcernedEntity, StrongGuid>
    {
      Defaulter = Defaulted,
      StreamPrefix = StreamPrefix
    };

  public string GetStreamName() => GetStreamName(Id);

  public async ValueTask<FirstDegreeConcernedEntity> Fold(
    FirstDegreeStartedDependingOnSecondDegree evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await fetcher
      .LatestFetch<SecondDegreeConcernedEntity>(new StrongGuid(evt.SecondDegreeId))
      .Map(e => e.Name)
      .Match(n => this with { SecondDegreeNames = [..SecondDegreeNames, n] }, () => this);

  public ValueTask<FirstDegreeConcernedEntity> Fold(
    FirstDegreeConcernedEntityEventAboutInterestedEntity evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this);

  public ValueTask<FirstDegreeConcernedEntity> Fold(
    FirstDegreeConcernedEntityTagged evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        Tags = Tags.Append(evt.Tag).Distinct().ToArray()
      });

  public async ValueTask<FirstDegreeConcernedEntity> Fold(
    SecondDegreeConcernedEntityNamed evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await fetcher
      .LatestFetch<SecondDegreeConcernedEntity>(new StrongGuid(evt.Id))
      .Match(e => this with { SecondDegreeNames = [..SecondDegreeNames, e.Name] }, () => this);

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";
  public static FirstDegreeConcernedEntity Defaulted(StrongGuid id) => new(id.Value, [], []);
}

public record FirstDegreeConcernedEntityTagged(Guid Id, string Tag) : EventModelEvent
{
  public string GetStreamName() => FirstDegreeConcernedEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record FirstDegreeConcernedEntityEventAboutInterestedEntity(Guid Id, Guid EntityThatDependsId)
  : EventModelEvent
{
  public string GetStreamName() => FirstDegreeConcernedEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}
