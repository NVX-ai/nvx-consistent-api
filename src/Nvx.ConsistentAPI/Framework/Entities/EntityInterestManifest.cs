namespace Nvx.ConsistentAPI;

public record EntityInterestManifest(
  string InterestedEntityStreamName,
  StrongId InterestedEntityId,
  string ConcernedEntityStreamName,
  StrongId ConcernedEntityId);
