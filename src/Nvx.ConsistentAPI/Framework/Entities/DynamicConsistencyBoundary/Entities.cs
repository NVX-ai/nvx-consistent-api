namespace Nvx.ConsistentAPI;

public record ConcernedEntityId(string ConcernedEntityStreamName) : StrongId
{
  public override string StreamId() => ToString();
  public override string ToString() => ConcernedEntityStreamName;
}

public partial record ConcernedEntityEntity(
  string ConcernedEntityStreamName,
  (string name, Dictionary<string, string> id)[] InterestedStreams)
  : EventModelEntity<ConcernedEntityEntity>,
    Folds<ConcernedEntityReceivedInterest, ConcernedEntityEntity>,
    Folds<ConcernedEntityHadInterestRemoved, ConcernedEntityEntity>
{
  public const string StreamPrefix = "framework-concerned-stream-entity-";

  public static readonly EntityDefinition Definition = new EntityDefinition<ConcernedEntityEntity, ConcernedEntityId>
  {
    Defaulter = Defaulted,
    StreamPrefix = StreamPrefix
  };

  public string GetStreamName() => GetStreamName(new ConcernedEntityId(ConcernedEntityStreamName));

  public ValueTask<ConcernedEntityEntity> Fold(
    ConcernedEntityHadInterestRemoved evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        InterestedStreams = [.. InterestedStreams.Where(s => s.name != evt.InterestedEntityStreamName)]
      });

  public ValueTask<ConcernedEntityEntity> Fold(
    ConcernedEntityReceivedInterest evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        InterestedStreams = InterestedStreams
          .Append((evt.InterestedEntityStreamName, evt.InterestedEntityId))
          .Distinct()
          .ToArray()
      });

  public static string GetStreamName(ConcernedEntityId entityId) => $"{StreamPrefix}{entityId}";

  public static ConcernedEntityEntity Defaulted(ConcernedEntityId id) =>
    new(id.ConcernedEntityStreamName, []);
}

public record InterestedEntityId(string InterestedEntityStreamName) : StrongId
{
  public override string StreamId() => ToString();
  public override string ToString() => InterestedEntityStreamName;
}

public partial record InterestedEntityEntity(
  string InterestedEntityStreamName,
  string[] ConcernedStreamNames,
  string[] OriginatingEventIds)
  : EventModelEntity<InterestedEntityEntity>,
    Folds<InterestedEntityRegisteredInterest, InterestedEntityEntity>,
    Folds<InterestedEntityHadInterestRemoved, InterestedEntityEntity>
{
  public const string StreamPrefix = "framework-interested-stream-entity-";

  public static readonly EntityDefinition Definition = new EntityDefinition<InterestedEntityEntity, InterestedEntityId>
  {
    Defaulter = Defaulted,
    StreamPrefix = StreamPrefix
  };

  public string GetStreamName() => GetStreamName(new InterestedEntityId(InterestedEntityStreamName));

  public ValueTask<InterestedEntityEntity> Fold(
    InterestedEntityHadInterestRemoved evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        ConcernedStreamNames = [.. ConcernedStreamNames.Where(s => s != evt.ConcernedEntityStreamName)],
        OriginatingEventIds = OriginatingEventIds.Append(evt.OriginatingEventId).Distinct().ToArray()
      });

  public ValueTask<InterestedEntityEntity> Fold(
    InterestedEntityRegisteredInterest evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        ConcernedStreamNames = ConcernedStreamNames.Append(evt.ConcernedEntityStreamName).Distinct().ToArray(),
        OriginatingEventIds = OriginatingEventIds.Append(evt.OriginatingEventId).ToArray()
      });

  public static string GetStreamName(InterestedEntityId entityId) => $"{StreamPrefix}{entityId}";

  public static InterestedEntityEntity Defaulted(InterestedEntityId id) =>
    new(id.InterestedEntityStreamName, [], []);
}
