namespace ConsistentAPI;

public record EntityInterestManifest(
  string InterestedEntityStreamName,
  StrongId InterestedEntityId,
  string ConcernedEntityStreamName,
  StrongId ConcernedEntityId);
