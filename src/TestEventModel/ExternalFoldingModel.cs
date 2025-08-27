using Nvx.ConsistentAPI;

namespace TestEventModel;

public static class ExternalFoldingModel
{
  public static readonly EventModel Model = new()
  {
    Entities =
    [
      EntityThatDepends.Definition,
      EntityDependedOn.Definition,
      EntityDependedOnEntityDependedOn.Definition
    ],
    ReadModels =
    [
      EntityThatDependsReadModel.Definition
    ],
    InterestTriggers =
    [
      new InitiatesInterest<EntityThatDependsOnReceivedDependency>(evt =>
      [
        new EntityInterestManifest(
          evt.GetStreamName(),
          evt.GetEntityId(),
          EntityDependedOn.GetStreamName(evt.DependsOnId),
          new StrongGuid(evt.DependsOnId))
      ]),
      new StopsInterest<EntityThatDependsOnRemovedDependency>(evt =>
      [
        new EntityInterestManifest(
          evt.GetStreamName(),
          evt.GetEntityId(),
          EntityDependedOn.GetStreamName(evt.DependsOnId),
          new StrongGuid(evt.DependsOnId))
      ]),
      new InitiatesInterest<EntityDependedOnHeardAboutEntityThatDepends>(evt =>
      [
        new EntityInterestManifest(
          EntityThatDepends.GetStreamName(evt.EntityThatDependsId),
          new StrongGuid(evt.EntityThatDependsId),
          evt.GetStreamName(),
          evt.GetEntityId())
      ]),
      new InitiatesInterest<EntityDependedOnStartedDependingOn>(evt =>
      [
        new EntityInterestManifest(
          EntityDependedOn.GetStreamName(evt.Id),
          new StrongGuid(evt.Id),
          EntityDependedOnEntityDependedOn.GetStreamName(evt.FartherDependentId),
          new StrongGuid(evt.FartherDependentId))
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
    new ReadModelDefinition<EntityThatDependsReadModel, EntityThatDepends>
    {
      StreamPrefix = EntityThatDepends.StreamPrefix,
      Projector = From,
      AreaTag = "TestExternalFolding"
    };

  public StrongId GetStrongId() => new StrongGuid(EntityId);

  public static EntityThatDependsReadModel[] From(EntityThatDepends entity) =>
    [new(entity.Id.ToString(), entity.Id, entity.DependedOnIds, entity.DependedOnTags, entity.FarAwayNames)];
}

public partial record EntityThatDepends(Guid Id, Guid[] DependedOnIds, string[] DependedOnTags, string[] FarAwayNames)
  : EventModelEntity<EntityThatDepends>,
    Folds<EntityThatDependsOnReceivedDependency, EntityThatDepends>,
    Folds<EntityThatDependsOnRemovedDependency, EntityThatDepends>,
    FoldsExternally<EntityDependedOnTagged, EntityThatDepends>,
    FoldsExternally<EntityDependedOnHeardAboutEntityThatDepends, EntityThatDepends>
{
  public const string StreamPrefix = "entity-that-depends-entity-";

  public static readonly EntityDefinition Definition =
    new EntityDefinition<EntityThatDepends, StrongGuid>
    {
      Defaulter = Defaulted,
      StreamPrefix = StreamPrefix
    };

  public string GetStreamName() => GetStreamName(Id);

  public async ValueTask<EntityThatDepends> Fold(
    EntityThatDependsOnReceivedDependency evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await DependedOnIds
      .Append(evt.DependsOnId)
      .Distinct()
      .ToArray()
      .Apply(async ids =>
        this with
        {
          DependedOnIds = ids,
          DependedOnTags = await GetTags(fetcher, ids)
        });

  public async ValueTask<EntityThatDepends> Fold(
    EntityThatDependsOnRemovedDependency evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await DependedOnIds
      .Where(s => s != evt.DependsOnId)
      .Distinct()
      .ToArray()
      .Apply(async ids =>
        this with
        {
          DependedOnIds = ids,
          DependedOnTags = await GetTags(fetcher, ids)
        });

  public async ValueTask<EntityThatDepends> Fold(
    EntityDependedOnHeardAboutEntityThatDepends evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    this with
    {
      DependedOnIds = DependedOnIds.Append(evt.Id).Distinct().ToArray(),
      FarAwayNames = FarAwayNames
        .Concat(
          await fetcher.LatestFetch<EntityDependedOn>(new StrongGuid(evt.Id)).Map(e => e.FarNames).DefaultValue([]))
        .Distinct()
        .ToArray()
    };

  public async ValueTask<EntityThatDepends> Fold(
    EntityDependedOnTagged evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    this with { DependedOnTags = await GetTags(fetcher, DependedOnIds) };

  private static async Task<string[]> GetTags(RevisionFetcher fetcher, Guid[] ids) =>
    await ids
      .Select<Guid, Func<Task<string[]>>>(id => async () =>
        await fetcher
          .LatestFetch<EntityDependedOn>(new StrongGuid(id))
          .Match(
            edo => edo.Tags,
            () => []
          ))
      .Parallel()
      .Map(nestedIds => nestedIds.SelectMany(Prelude.Id).ToArray());

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";
  public static EntityThatDepends Defaulted(StrongGuid id) => new(id.Value, [], [], []);
}

public record EntityThatDependsOnReceivedDependency(Guid Id, Guid DependsOnId) : EventModelEvent
{
  public string GetStreamName() => EntityThatDepends.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record EntityThatDependsOnRemovedDependency(Guid Id, Guid DependsOnId) : EventModelEvent
{
  public string GetStreamName() => EntityThatDepends.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record EntityDependedOnEntityDependedOnCreated(Guid Id, string Name) : EventModelEvent
{
  public string GetStreamName() => EntityDependedOnEntityDependedOn.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record EntityDependedOnStartedDependingOn(Guid Id, Guid FartherDependentId) : EventModelEvent
{
  public string GetStreamName() => EntityDependedOn.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

partial record EntityDependedOnEntityDependedOn(Guid Id, string Name)
  : EventModelEntity<EntityDependedOnEntityDependedOn>,
    Folds<EntityDependedOnEntityDependedOnCreated, EntityDependedOnEntityDependedOn>
{
  public const string StreamPrefix = "entity-further-depended-on-entity-depended-on-entity-";

  public static readonly EntityDefinition Definition =
    new EntityDefinition<EntityDependedOnEntityDependedOn, StrongGuid>
    {
      Defaulter = Defaulted,
      StreamPrefix = StreamPrefix
    };

  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<EntityDependedOnEntityDependedOn> Fold(
    EntityDependedOnEntityDependedOnCreated evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this with { Name = evt.Name });


  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";

  public static EntityDependedOnEntityDependedOn Defaulted(StrongGuid id) => new(id.Value, string.Empty);
}

public partial record EntityDependedOn(Guid Id, string[] Tags, string[] FarNames)
  : EventModelEntity<EntityDependedOn>,
    Folds<EntityDependedOnTagged, EntityDependedOn>,
    Folds<EntityDependedOnHeardAboutEntityThatDepends, EntityDependedOn>,
    Folds<EntityDependedOnStartedDependingOn, EntityDependedOn>
{
  public const string StreamPrefix = "entity-depended-on-entity-";

  public static readonly EntityDefinition Definition =
    new EntityDefinition<EntityDependedOn, StrongGuid>
    {
      Defaulter = Defaulted,
      StreamPrefix = StreamPrefix
    };

  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<EntityDependedOn> Fold(
    EntityDependedOnHeardAboutEntityThatDepends evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this);

  public async ValueTask<EntityDependedOn> Fold(
    EntityDependedOnStartedDependingOn evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await fetcher
      .LatestFetch<EntityDependedOnEntityDependedOn>(new StrongGuid(evt.FartherDependentId))
      .Map(e => e.Name)
      .Match(n => this with { FarNames = [..FarNames, n] }, () => this);

  public ValueTask<EntityDependedOn> Fold(
    EntityDependedOnTagged evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        Tags = Tags.Append(evt.Tag).Distinct().ToArray()
      });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";
  public static EntityDependedOn Defaulted(StrongGuid id) => new(id.Value, [], []);
}

public record EntityDependedOnTagged(Guid Id, string Tag) : EventModelEvent
{
  public string GetStreamName() => EntityDependedOn.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record EntityDependedOnHeardAboutEntityThatDepends(Guid Id, Guid EntityThatDependsId)
  : EventModelEvent
{
  public string GetStreamName() => EntityDependedOn.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}
