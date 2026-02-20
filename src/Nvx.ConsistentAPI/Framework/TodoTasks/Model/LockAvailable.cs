namespace Nvx.ConsistentAPI;

/// <summary>
/// Marker type indicating that a todo's lock is available for acquisition.
/// Used as the success type in <see cref="ProcessorEntity.LockState"/> to enforce
/// that a lock availability check has been performed before proceeding.
/// </summary>
public class LockAvailable
{
  internal LockAvailable() { }
}
