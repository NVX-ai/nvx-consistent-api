namespace Nvx.ConsistentAPI.Framework.Projections.Model;

public partial record ProjectionTrackerEntity(
  string Version,
  string[] ExistingProjections,
  string[] UpToDateProjections,
  ulong? Checkpoint) : EventModelEntity<ProjectionTrackerEntity>,
  Folds<ProjectionRegistered, ProjectionTrackerEntity>,
  Folds<ProjectionCheckpointReached, ProjectionTrackerEntity>,
  Folds<ProjectionUpToDate, ProjectionTrackerEntity>,
  Folds<ProjectionReset, ProjectionTrackerEntity>,
  Folds<ProjectionSnapshotReached, ProjectionTrackerEntity>
{
  public const string StreamPrefix = "framework-projection-tracker-";

  public static readonly EntityDefinition Definition =
    new EntityDefinition<ProjectionTrackerEntity, ProjectionTrackerId>
    {
      Defaulter = Defaulted,
      StreamPrefix = StreamPrefix
    };

  public string GetStreamName() => GetStreamName(Version);

  public ValueTask<ProjectionTrackerEntity> Fold(
    ProjectionCheckpointReached evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this with { Checkpoint = evt.Checkpoint });

  public ValueTask<ProjectionTrackerEntity> Fold(
    ProjectionRegistered evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { ExistingProjections = [..ExistingProjections, evt.ProjectionName] });

  public ValueTask<ProjectionTrackerEntity> Fold(
    ProjectionReset evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        UpToDateProjections = UpToDateProjections.Where(p => p != evt.ProjectionName).ToArray()
      });

  public ValueTask<ProjectionTrackerEntity> Fold(
    ProjectionSnapshotReached evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      new ProjectionTrackerEntity(evt.Version, evt.ExistingProjections, evt.UpToDateProjections, evt.Checkpoint));

  public ValueTask<ProjectionTrackerEntity> Fold(
    ProjectionUpToDate evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { UpToDateProjections = [..UpToDateProjections, evt.ProjectionName] });

  public static ProjectionTrackerEntity Defaulted(ProjectionTrackerId id) => new(id.Version, [], [], null);

  public static string GetStreamName(string version) => $"{StreamPrefix}{version}";
}

public record ProjectionTrackerId(string Version) : StrongId
{
  public override string StreamId() => Version;
  public override string ToString() => StreamId();
}
