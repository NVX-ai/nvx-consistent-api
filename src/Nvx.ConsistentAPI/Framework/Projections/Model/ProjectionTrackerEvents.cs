namespace Nvx.ConsistentAPI.Framework.Projections.Model;

public record ProjectionRegistered(string Version, string ProjectionName) : EventModelEvent
{
  public string GetSwimlane() => ProjectionTrackerEntity.StreamPrefix;
  public StrongId GetEntityId() => new ProjectionTrackerId(Version);
}

public record ProjectionCheckpointReached(string Version, ulong Checkpoint) : EventModelEvent
{
  public string GetSwimlane() => ProjectionTrackerEntity.StreamPrefix;
  public StrongId GetEntityId() => new ProjectionTrackerId(Version);
}

public record ProjectionUpToDate(string Version, string ProjectionName) : EventModelEvent
{
  public string GetSwimlane() => ProjectionTrackerEntity.StreamPrefix;
  public StrongId GetEntityId() => new ProjectionTrackerId(Version);
}

public record ProjectionReset(string Version, string ProjectionName) : EventModelEvent
{
  public string GetSwimlane() => ProjectionTrackerEntity.StreamPrefix;
  public StrongId GetEntityId() => new ProjectionTrackerId(Version);
}

public record ProjectionSnapshotReached(
  string Version,
  string[] ExistingProjections,
  string[] UpToDateProjections,
  ulong? Checkpoint) : EventModelSnapshotEvent
{
  public string GetSwimlane() => ProjectionTrackerEntity.StreamPrefix;
  public StrongId GetEntityId() => new ProjectionTrackerId(Version);
}
