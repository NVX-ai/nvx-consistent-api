namespace Nvx.ConsistentAPI;

/// <summary>
/// Possible outcomes when a todo task action completes.
/// </summary>
public enum TodoOutcome
{
  Retry,
  Done,
  Locked
}
