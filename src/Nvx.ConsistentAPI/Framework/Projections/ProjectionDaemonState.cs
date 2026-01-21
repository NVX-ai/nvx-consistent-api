namespace Nvx.ConsistentAPI.Framework.Projections;

/// <summary>
/// Maintains the runtime state of the projection daemon, including progress tracking
/// and synchronization primitives. This class centralizes all mutable state used by
/// the projection system to track catch-up progress, subscription position, and
/// projection counts.
/// </summary>
/// <remarks>
/// Thread-safety is ensured through:
/// - <see cref="CatchUpLock"/> semaphore for catch-up synchronization
/// - <see cref="Interlocked"/> operations for counter increments
/// </remarks>
public class ProjectionDaemonState
{
  /// <summary>
  /// Version identifier for the projection subscription. Used to track projection
  /// tracker entities and allow for subscription versioning/migration.
  /// </summary>
  public const string SubscriptionVersion = "1";

  /// <summary>
  /// Semaphore used to ensure only one catch-up operation runs at a time.
  /// This prevents concurrent catch-up processes from duplicating work or
  /// causing race conditions when updating projection state.
  /// </summary>
  public static readonly SemaphoreSlim CatchUpLock = new(1);

  private string[] catchingUp = [];
  private bool isProjecting;
  private ulong lastCatchUpProcessedPosition;
  private ulong lastProcessedPosition;
  private int projectedCount;

  /// <summary>
  /// Names of projections currently being caught up. Empty array when no
  /// catch-up is in progress. Used for monitoring and insight reporting.
  /// </summary>
  public string[] CatchingUp
  {
    get => catchingUp;
    set => catchingUp = value;
  }

  /// <summary>
  /// Indicates whether a projection is currently being processed by the
  /// subscription handler. Used to show active processing status in insights.
  /// </summary>
  public bool IsProjecting
  {
    get => isProjecting;
    set => isProjecting = value;
  }

  /// <summary>
  /// The last event store commit position processed during catch-up operations.
  /// Used to calculate catch-up progress percentage in insights.
  /// </summary>
  public ulong LastCatchUpProcessedPosition
  {
    get => lastCatchUpProcessedPosition;
    set => lastCatchUpProcessedPosition = value;
  }

  /// <summary>
  /// The last event store commit position processed by the live subscription.
  /// Used to calculate overall daemon progress percentage in insights.
  /// </summary>
  public ulong LastProcessedPosition
  {
    get => lastProcessedPosition;
    set => lastProcessedPosition = value;
  }

  /// <summary>
  /// Total count of events that have been projected since daemon startup.
  /// Incremented atomically via <see cref="IncrementProjectedCount"/>.
  /// </summary>
  public int ProjectedCount => projectedCount;

  /// <summary>
  /// Thread-safe increment of the projected event counter.
  /// Called after each successful projection operation.
  /// </summary>
  public void IncrementProjectedCount() => Interlocked.Increment(ref projectedCount);
}
