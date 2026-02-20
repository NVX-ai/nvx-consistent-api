using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Framework;
using Nvx.ConsistentAPI.Framework.DaemonCoordination;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Background worker that processes entries from the HydrationQueue table, fetching entity state
/// from the event store and updating read models. Implements distributed locking to allow multiple
/// workers to process the queue concurrently without conflicts.
/// </summary>
/// <remarks>
/// <para>
/// The hydration process follows these steps:
/// <list type="number">
///   <item>Poll the queue for an unlocked entry and atomically lock it</item>
///   <item>Fetch the entity from the event store</item>
///   <item>For each applicable read model, acquire a table-level lock and project the entity</item>
///   <item>Mark the entry as hydrated with the last processed position</item>
/// </list>
/// </para>
/// <para>
/// Locking strategy:
/// <list type="bullet">
///   <item>Stream locks (90 seconds) prevent multiple workers from hydrating the same stream</item>
///   <item>Read model locks coordinate which deployment instance owns a read model table</item>
///   <item>Locks are refreshed every 30 seconds during long-running hydrations</item>
///   <item>Entries are abandoned after 25 failed attempts (TimesLocked >= 25)</item>
/// </list>
/// </para>
/// </remarks>
public class HydrationDaemonWorker
{
  #region SQL Scripts - Table and Index Creation

  /// <summary>
  /// Creates the HydrationQueue table if it doesn't exist. This table stores pending hydration
  /// requests with stream information, position tracking, and distributed locking fields.
  /// </summary>
  private const string QueueTableCreationSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'HydrationQueue')
    BEGIN
    CREATE TABLE [dbo].[HydrationQueue](
        [StreamName] [nvarchar](3000) NOT NULL,
        [SerializedId] [nvarchar](max) NOT NULL,
        [IdTypeName] [nvarchar](256) NOT NULL,
        [IdTypeNamespace] [nvarchar](256) NULL,
        [ModelHash] [nvarchar](256) NOT NULL,
        [Position] NUMERIC(20,0) NOT NULL,
        [WorkerId] [uniqueidentifier] NULL,
        [LockedUntil] [datetime2](7) NULL,
        [TimesLocked] [int] NOT NULL,
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT (GETUTCDATE()),
        [IsDynamicConsistencyBoundary] [bit] NOT NULL DEFAULT (0),
        [LastHydratedPosition] NUMERIC(20,0) NULL DEFAULT NULL)
    END
    """;

  /// <summary>
  /// Index optimized for finding hydration candidates ordered by priority.
  /// </summary>
  private const string GetCandidatesIndexCreationSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_HydrationQueue_GetCandidates')
    BEGIN
      CREATE NONCLUSTERED INDEX [IX_HydrationQueue_GetCandidates]
      ON [dbo].[HydrationQueue] ([ModelHash], [TimesLocked], [IsDynamicConsistencyBoundary], [Position])
      INCLUDE ([LockedUntil], [LastHydratedPosition]);
    END
    """;

  /// <summary>
  /// Index optimized for the atomic lock acquisition query.
  /// </summary>
  private const string TryLockIndexCreationSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_HydrationQueue_TryLock')
    BEGIN
      CREATE NONCLUSTERED INDEX [IX_HydrationQueue_TryLock]
      ON [dbo].[HydrationQueue] ([StreamName], [ModelHash], [Position], [TimesLocked])
      INCLUDE ([LockedUntil], [WorkerId]);
    END
    """;

  /// <summary>
  /// Index for querying last hydrated position across streams.
  /// </summary>
  private const string LastHydratedPositionForStreamIndexCreationSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_HydrationQueue_LastHydratedPositionForStream')
    BEGIN
      CREATE NONCLUSTERED INDEX [IX_HydrationQueue_LastHydratedPositionForStream]
      ON [dbo].[HydrationQueue] ([TimesLocked], [Position])
      INCLUDE ([LastHydratedPosition], [StreamName])
      WITH (ONLINE = ON);
    END
    """;

  /// <summary>
  /// Creates the ModelHashReadModelLocks table for coordinating read model ownership across
  /// deployment instances. Only one model hash (deployment) can write to a read model at a time.
  /// </summary>
  private const string ModelHashReadModelLockTableCreationSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ModelHashReadModelLocks')
    BEGIN
    CREATE TABLE [dbo].[ModelHashReadModelLocks](
        [ModelHash] [nvarchar](256) NOT NULL PRIMARY KEY,
        [ReadModelName] [nvarchar](256) NOT NULL,
        [LockedUntil] [datetime2](7) NOT NULL
    )
    END
    """;

  /// <summary>
  /// Index for looking up which model hash owns a read model.
  /// </summary>
  private const string ModelHashReadModelLocksIndexCreationSql =
    """
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ModelHashReadModelLocks_ReadModelName')
    BEGIN
      CREATE NONCLUSTERED INDEX [IX_ModelHashReadModelLocks_ReadModelName]
      ON [dbo].[ModelHashReadModelLocks] ([ReadModelName])
      INCLUDE ([ModelHash], [LockedUntil]);
    END
    """;

  #endregion

  #region Constants

  /// <summary>
  /// Duration in seconds that a stream lock is held. Workers must refresh locks before expiry.
  /// </summary>
  private const int StreamLockLengthSeconds = 90;

  /// <summary>
  /// How often (in seconds) to refresh stream locks during long-running hydrations.
  /// Set to 1/3 of lock length to ensure timely refresh.
  /// </summary>
  private const int RefreshStreamLockFrequencySeconds = StreamLockLengthSeconds / 3;

  #endregion

  #region SQL Scripts - Queue Operations

  /// <summary>
  /// Updates the queue entry after successful hydration, clearing the lock and recording
  /// the last hydrated position for future comparison.
  /// </summary>
  private const string UpdateHydrationState =
    """
    UPDATE [HydrationQueue] WITH (ROWLOCK)
    SET [WorkerId] = NULL,
        [LockedUntil] = NULL,
        [LastHydratedPosition] = @LastHydratedPosition
    WHERE [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
      AND [WorkerId] = @WorkerId
      AND [Position] = @Position
    """;

  /// <summary>
  /// Upserts a hydration request into the queue. If an entry exists for the stream/model hash
  /// combination, updates the position and resets the retry counter. Preserves the
  /// IsDynamicConsistencyBoundary flag if already set (sticky flag).
  /// </summary>
  private const string UpsertSql =
    """
    MERGE [HydrationQueue] WITH (ROWLOCK) AS target
    USING (
      SELECT
        @StreamName AS StreamName,
        @ModelHash AS ModelHash,
        @Position AS Position,
        @SerializedId AS SerializedId,
        @IdTypeName AS IdTypeName,
        @IdTypeNamespace AS IdTypeNamespace,
        @IsDynamicConsistencyBoundary AS IsDynamicConsistencyBoundary
    ) AS source
    ON  target.[StreamName] = source.StreamName
    AND target.[ModelHash] = source.ModelHash
    WHEN MATCHED THEN
        UPDATE SET
          [TimesLocked] = 0,
          [Position] = source.Position,
          [IsDynamicConsistencyBoundary] =
            CASE
              WHEN target.[IsDynamicConsistencyBoundary] = 1
              THEN 1
              ELSE source.IsDynamicConsistencyBoundary END
    WHEN NOT MATCHED THEN
        INSERT (
          [StreamName],
          [SerializedId],
          [IdTypeName],
          [IdTypeNamespace],
          [ModelHash],
          [Position],
          [TimesLocked],
          [IsDynamicConsistencyBoundary]
        )
        VALUES (
          source.StreamName,
          source.SerializedId,
          source.IdTypeName,
          source.IdTypeNamespace,
          source.ModelHash,
          source.Position,
          0,
          source.IsDynamicConsistencyBoundary
        );
    """;

  /// <summary>
  /// Counts entries that still need hydration (not yet hydrated or position has advanced).
  /// Used to determine if the daemon is up-to-date.
  /// </summary>
  private const string PendingEventsCountSql =
    """
    SELECT COUNT(*)
    FROM [HydrationQueue]
    WHERE [ModelHash] = @ModelHash
      AND [TimesLocked] < 25
      AND ([LastHydratedPosition] IS NULL OR [Position] > [LastHydratedPosition]);
    """;

  /// <summary>
  /// Counts pending entries up to a specific position. Used by TodoProcessor to check
  /// if read models are caught up to a todo's event position before executing.
  /// </summary>
  private const string PendingEventsBeforePositionCountSql =
    """
    SELECT COUNT(*)
    FROM [HydrationQueue]
    WHERE [ModelHash] = @ModelHash
      AND [Position] <= @Position
      AND [TimesLocked] < 25
      AND ([LastHydratedPosition] IS NULL OR [Position] > [LastHydratedPosition]);
    """;

  /// <summary>
  /// Releases a lock without updating position. Used when hydration fails or is abandoned.
  /// </summary>
  private const string ReleaseSql =
    """
    UPDATE [HydrationQueue] WITH (ROWLOCK)
    SET [WorkerId] = NULL,
        [LockedUntil] = NULL
    WHERE [WorkerId] = @WorkerId
      AND [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
    """;

  /// <summary>
  /// Deletes all queue entries for streams matching a prefix. Used when resetting
  /// a read model to replay from scratch.
  /// </summary>
  private const string ResetStreamSql =
    """
    DELETE FROM [HydrationQueue] WITH (ROWLOCK)
    WHERE [StreamName] LIKE @StreamPrefix + '%'
      AND [ModelHash] = @ModelHash
    """;

  /// <summary>
  /// Gets the last hydrated position for a stream by another model hash.
  /// Used to coordinate between different deployment instances.
  /// </summary>
  private const string GetStreamLastHydratedPositionByHashSql =
    """
    SELECT TOP 1 [LastHydratedPosition]
    FROM [HydrationQueue]
    WHERE [StreamName] = @StreamName
      AND [ModelHash] = @ModelHash
    ORDER BY [LastHydratedPosition] DESC
    """;

  /// <summary>
  /// Atomically selects and locks a candidate for hydration using READPAST to skip locked rows.
  /// Prioritizes non-DCB entries (IsDynamicConsistencyBoundary ASC) and higher positions (DESC).
  /// Returns the locked entry via OUTPUT clause.
  /// </summary>
  private const string LockCandidateSql =
    """
    ;WITH cte AS (
      SELECT TOP 1 *
      FROM [HydrationQueue] WITH (ROWLOCK, READPAST)
      WHERE ([LockedUntil] IS NULL OR [LockedUntil] < GETUTCDATE())
        AND [ModelHash] = @ModelHash
        AND [TimesLocked] < 25
        AND ([LastHydratedPosition] IS NULL OR [Position] > [LastHydratedPosition])
      ORDER BY [IsDynamicConsistencyBoundary] ASC, [Position] DESC
    )
    UPDATE cte
    SET
      [WorkerId] = @WorkerId,
      [LockedUntil] = DATEADD(SECOND, @lockLength, GETUTCDATE()),
      [TimesLocked] = [TimesLocked] + 1
    OUTPUT
      inserted.[StreamName],
      inserted.[SerializedId],
      inserted.[IdTypeName],
      inserted.[IdTypeNamespace],
      inserted.[ModelHash],
      inserted.[Position],
      inserted.[WorkerId],
      inserted.[LockedUntil],
      inserted.[TimesLocked],
      inserted.[CreatedAt],
      inserted.[IsDynamicConsistencyBoundary],
      inserted.[LastHydratedPosition];
    """;

  #endregion

  #region SQL Scripts - Read Model Locking

  /// <summary>
  /// Inserts an initial read model lock if one doesn't exist. Used during startup to
  /// claim ownership of read models.
  /// </summary>
  private static readonly string SafeInsertModelHashReadModelLockSql =
    $"""
     INSERT INTO [ModelHashReadModelLocks] WITH (ROWLOCK)
     SELECT
       @ModelHash AS ModelHash,
       @ReadModelName AS ReadModelName,
       DATEADD(SECOND, {StreamLockLengthSeconds}, GETUTCDATE()) AS LockedUntil
     WHERE NOT EXISTS (SELECT TOP 1 1 FROM [ModelHashReadModelLocks] WHERE ModelHash = @ModelHash)
     """;

  /// <summary>
  /// Attempts to acquire or refresh a read model lock. Succeeds if the lock is expired,
  /// unset, or already owned by this model hash.
  /// </summary>
  private static readonly string TryModelHashReadModelLockSql =
    $"""
      UPDATE [ModelHashReadModelLocks] WITH (ROWLOCK)
      SET [LockedUntil] = DATEADD(SECOND, {StreamLockLengthSeconds}, GETUTCDATE())
      WHERE
        [ReadModelName] = @ReadModelName
        AND
         [LockedUntil] IS NULL
         OR [LockedUntil] < GETUTCDATE()
         OR [ModelHash] = @ModelHash
     """;

  /// <summary>
  /// Refreshes an existing stream lock. Only succeeds if we still own the lock
  /// (WorkerId matches and lock hasn't expired).
  /// </summary>
  private static readonly string TryRefreshStreamLockSql =
    $"""
     UPDATE [HydrationQueue] WITH (ROWLOCK)
     SET [LockedUntil] = DATEADD(SECOND, {StreamLockLengthSeconds}, GETUTCDATE())
     WHERE [WorkerId] = @WorkerId
       AND [StreamName] = @StreamName
       AND [ModelHash] = @ModelHash
       AND [LockedUntil] > GETUTCDATE()
     """;

  #endregion

  /// <summary>
  /// SQL scripts required to create the hydration queue schema. Execute during application startup.
  /// </summary>
  public static readonly string[] TableCreationScripts =
  [
    QueueTableCreationSql,
    GetCandidatesIndexCreationSql,
    TryLockIndexCreationSql,
    ModelHashReadModelLockTableCreationSql,
    ModelHashReadModelLocksIndexCreationSql
  ];

  /// <summary>
  /// Additional index scripts that can be created online after initial deployment.
  /// </summary>
  public static readonly string[] IndexCreationScripts = [LastHydratedPositionForStreamIndexCreationSql];

  #region Instance Fields

  private readonly string connectionString;
  private readonly DatabaseHandlerFactory dbFactory;
  private readonly Fetcher fetcher;
  private readonly ILogger logger;

  /// <summary>
  /// Unique identifier for this deployment's schema version. Different model hashes
  /// indicate incompatible read model schemas that shouldn't share data.
  /// </summary>
  private readonly string modelHash;

  private readonly IdempotentReadModel[] readModels;

  // ReSharper disable once NotAccessedField.Local
  private readonly Task task;

  /// <summary>
  /// Semaphore protecting access to ResumePollingAt for thread-safe wake-up signaling.
  /// </summary>
  private readonly SemaphoreSlim wakeUpSemaphore = new(1, 1);

  /// <summary>
  /// Unique identifier for this worker instance, used for distributed locking.
  /// </summary>
  private readonly Guid workerId = Guid.NewGuid();

  private DateTime resumePollingAt = DateTime.MaxValue;

  /// <summary>
  /// Exponential backoff multiplier. Increases when queue is empty, resets on wake-up.
  /// </summary>
  private int waitBackoff = 1;

  #endregion

  /// <summary>
  /// Initializes and starts a new hydration worker that continuously polls the queue.
  /// </summary>
  /// <param name="modelHash">Schema version identifier for read model compatibility.</param>
  /// <param name="connectionString">SQL Server connection string for the read model database.</param>
  /// <param name="fetcher">Service for fetching entities from the event store.</param>
  /// <param name="readModels">Read models this worker can hydrate.</param>
  /// <param name="dbFactory">Factory for creating database handlers during projection.</param>
  /// <param name="messageHub">Message hub for subscribing to wake-up signals.</param>
  /// <param name="logger">Logger for diagnostic output.</param>
  public HydrationDaemonWorker(
    string modelHash,
    string connectionString,
    Fetcher fetcher,
    IdempotentReadModel[] readModels,
    DatabaseHandlerFactory dbFactory,
    MessageHub messageHub,
    ILogger logger)
  {
    this.modelHash = modelHash;
    this.connectionString = connectionString;
    this.fetcher = fetcher;
    this.readModels = readModels;
    this.dbFactory = dbFactory;
    this.logger = logger;
    task = Task.Run(Process);
    messageHub.Subscribe(this);
    this.logger.LogInformation("Hydration worker with ID {ID} started", workerId);
  }

  /// <summary>
  /// Gets or sets when polling should resume. Setting this also adjusts the backoff multiplier:
  /// DateTime.MinValue resets backoff to 1, any other value increments it.
  /// </summary>
  private DateTime ResumePollingAt
  {
    get => resumePollingAt;
    set
    {
      waitBackoff = value == DateTime.MinValue ? 1 : waitBackoff + 1;
      resumePollingAt = value;
    }
  }

  private bool ShouldPoll => ResumePollingAt <= DateTime.UtcNow;

  #region Static Registration Methods

  /// <summary>
  /// Registers a stream for hydration by inserting or updating a queue entry.
  /// Called by ReadModelHydrationDaemon when it encounters events that need read model updates.
  /// </summary>
  /// <param name="modelHash">Schema version identifier.</param>
  /// <param name="connectionString">Database connection string.</param>
  /// <param name="streamName">Event stream that needs hydration.</param>
  /// <param name="id">Strongly-typed entity identifier.</param>
  /// <param name="position">Global position of the triggering event.</param>
  /// <param name="isDynamicConsistencyBoundary">Whether this is from a DCB interest event.</param>
  /// <param name="readModels">Read models to check for applicability.</param>
  public static async Task Register(
    string modelHash,
    string connectionString,
    string streamName,
    StrongId id,
    ulong position,
    bool isDynamicConsistencyBoundary,
    IdempotentReadModel[] readModels)
  {
    // Skip registration if no read models care about this stream
    if (!readModels.Any(rm => rm.CanProject(streamName)))
    {
      return;
    }

    try
    {
      var idType = id.GetType();
      await using var connection = new SqlConnection(connectionString);
      await connection.ExecuteAsync(
        UpsertSql,
        new
        {
          StreamName = streamName,
          SerializedId = Serialization.Serialize(id),
          IdTypeName = idType.Name,
          IdTypeNamespace = idType.Namespace,
          ModelHash = modelHash,
          Position = Convert.ToDecimal(position),
          IsDynamicConsistencyBoundary = isDynamicConsistencyBoundary
        });
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      await Register(modelHash, connectionString, streamName, id, position, isDynamicConsistencyBoundary, readModels);
    }
  }

  /// <summary>
  /// Removes all queue entries for streams matching a prefix. Used when a read model
  /// is being rebuilt from scratch.
  /// </summary>
  public static async Task ResetStream(
    string modelHash,
    string connectionString,
    string streamPrefix)
  {
    try
    {
      await using var connection = new SqlConnection(connectionString);
      await connection.ExecuteAsync(
        ResetStreamSql,
        new
        {
          StreamPrefix = streamPrefix,
          ModelHash = modelHash
        });
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      await ResetStream(modelHash, connectionString, streamPrefix);
    }
  }

  /// <summary>
  /// Attempts to insert an initial lock for a read model during startup.
  /// Does nothing if a lock already exists for this model hash.
  /// </summary>
  public static async Task TryInitialLockReadModel(
    string modelHash,
    string readModelName,
    SqlConnection connection)
  {
    try
    {
      await connection.ExecuteAsync(
        SafeInsertModelHashReadModelLockSql,
        new
        {
          ModelHash = modelHash,
          ReadModelName = readModelName
        });
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      await TryInitialLockReadModel(modelHash, readModelName, connection);
    }
  }

  #endregion

  #region Read Model Locking

  /// <summary>
  /// Attempts to acquire or refresh a lock on a read model table.
  /// </summary>
  /// <returns>True if lock was acquired/refreshed, false if another model hash owns it.</returns>
  private async Task<bool> TryLockReadModel(string readModelName)
  {
    try
    {
      await using var connection = new SqlConnection(connectionString);
      return await connection.ExecuteAsync(
               TryModelHashReadModelLockSql,
               new
               {
                 ModelHash = modelHash,
                 ReadModelName = readModelName
               })
             > 0;
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      return await TryLockReadModel(readModelName);
    }
  }

  /// <summary>
  /// Gets the last hydrated position for a stream as recorded by another model hash.
  /// Used to coordinate with other deployments sharing the same database.
  /// </summary>
  private async Task<ulong?> GetStreamLastHydratedPositionByHash(string otherHash, string streamName)
  {
    try
    {
      await using var connection = new SqlConnection(connectionString);
      var result = await connection.QuerySingleOrDefaultAsync<decimal?>(
        GetStreamLastHydratedPositionByHashSql,
        new { StreamName = streamName, ModelHash = otherHash });
      return result.HasValue ? Convert.ToUInt64(result.Value) : null;
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      return await GetStreamLastHydratedPositionByHash(otherHash, streamName);
    }
  }

  /// <summary>
  /// Looks up which model hash currently owns a read model.
  /// </summary>
  private async Task<ForeignReadModelLock?> TryGetForeignHash(string readModelName)
  {
    try
    {
      await using var connection = new SqlConnection(connectionString);
      return await connection.QuerySingleOrDefaultAsync<ForeignReadModelLock>(
        "SELECT [ModelHash], [ReadModelName] FROM [ModelHashReadModelLocks] WHERE [ReadModelName] = @ReadModelName",
        new { ReadModelName = readModelName });
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      return await TryGetForeignHash(readModelName);
    }
  }

  #endregion

  #region Worker Loop

  /// <summary>
  /// Signals the worker to wake up immediately and check for work.
  /// Called by MessageHub when new events are registered.
  /// </summary>
  public void WakeUp()
  {
    wakeUpSemaphore.Wait();
    ResumePollingAt = DateTime.MinValue;
    wakeUpSemaphore.Release();
  }

  /// <summary>
  /// Main worker loop. Runs forever, polling for candidates when awake
  /// and sleeping with exponential backoff when the queue is empty.
  /// </summary>
  private async Task Process()
  {
    while (true)
    {
      try
      {
        if (!ShouldPoll)
        {
          await Task.Delay(Random.Shared.Next(1, 150));
          continue;
        }

        await TryProcess();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "error hydrating");
        await Task.Delay(Random.Shared.Next(1, 750));
      }
    }
    // ReSharper disable once FunctionNeverReturns
  }

  /// <summary>
  /// Atomically selects and locks the next candidate from the queue.
  /// Uses READPAST to skip rows locked by other workers.
  /// </summary>
  /// <returns>The locked entry if one was found, null if queue is empty.</returns>
  private async Task<HydrationQueueEntry?> GetCandidate()
  {
    try
    {
      await using var connection = new SqlConnection(connectionString);
      var candidate =
        await connection.QuerySingleOrDefaultAsync<HydrationQueueEntry>(
          LockCandidateSql,
          new { ModelHash = modelHash, workerId, lockLength = StreamLockLengthSeconds });
      // Verify we actually got the lock (OUTPUT returns the row even if update affected 0 rows in some edge cases)
      return candidate?.WorkerId == workerId ? candidate : null;
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      return await GetCandidate();
    }
  }

  /// <summary>
  /// Attempts to process one hydration entry. If no candidates are available,
  /// schedules the next poll with exponential backoff.
  /// </summary>
  private async Task TryProcess()
  {
    var candidate = await GetCandidate();
    if (candidate is null)
    {
      // No work available - back off
      await wakeUpSemaphore.WaitAsync();
      ResumePollingAt = DateTime.UtcNow.AddMilliseconds(waitBackoff * 250 + Random.Shared.Next(1, 250));
      wakeUpSemaphore.Release();
      return;
    }

    // Find read models that can project this stream
    var ableReadModels = readModels.Where(rm => rm.CanProject(candidate.StreamName)).ToArray();
    if (ableReadModels.Length == 0)
    {
      // No applicable read models - mark as done
      await MarkAsHydrated(candidate with { LastHydratedPosition = candidate.Position });
      return;
    }

    // Start hydration with lock refresh loop
    var cancellationSource = new CancellationTokenSource();
    var hydrateTask = Hydrate(cancellationSource.Token);
    var nextRefreshAt = DateTime.UtcNow.AddSeconds(2);

    // Keep refreshing the lock while hydration is in progress
    while (!hydrateTask.IsCompleted)
    {
      if (nextRefreshAt < DateTime.UtcNow)
      {
        if (!await TryRefreshLock(candidate.StreamName))
        {
          // Lost the lock - another worker took over, abort
          await cancellationSource.CancelAsync();
          logger.LogWarning(
            "Lost lock on stream {Stream} at position {Position} for worker {WorkerId} and model {ModelHash}",
            candidate.StreamName,
            candidate.Position,
            workerId,
            modelHash);
          return;
        }

        nextRefreshAt = DateTime.UtcNow.AddSeconds(RefreshStreamLockFrequencySeconds);
      }

      // It might mask the error from the cancellation of the hydration.
      // ReSharper disable once MethodSupportsCancellation
      await Task.Delay(10);
    }

    // Update queue with hydration result
    var lastHydratedPosition = await hydrateTask;
    await MarkAsHydrated(
      candidate with
      {
        LastHydratedPosition =
        // If the hydration fails or succeeds past the position, use the last hydrated position, it can happen
        // that hydrating from dynamic consistency boundaries that the position is smaller than the interest event.
        // In that case, the position of the record is used.
        lastHydratedPosition is null || lastHydratedPosition >= candidate.Position
          ? lastHydratedPosition
          : candidate.Position
      });
    return;

    // Local function that performs the actual hydration work
    async Task<ulong?> Hydrate(CancellationToken cancellationToken)
    {
      // Fetch entity from event store
      var maybeEntity = await candidate
        .GetStrongId()
        .Async()
        .Bind(id => fetcher
          .DaemonFetch(id, candidate.StreamName, candidate.IsDynamicConsistencyBoundary, cancellationToken)
          .Map(e => (id, e)));

      var lockedByOtherHash = new List<ForeignReadModelLock>();

      foreach (var t in maybeEntity)
      {
        // Project to each applicable read model
        foreach (var readModel in ableReadModels)
        {
          // Try to acquire read model lock
          if (!await TryLockReadModel(readModel.TableName))
          {
            // Another model hash owns this read model - track it for position verification
            var owner = await TryGetForeignHash(readModel.TableName);
            if (owner != null && owner.ModelHash != modelHash)
            {
              lockedByOtherHash.Add(owner);
            }

            continue;
          }

          // Process the read model with periodic lock refresh
          var processTask = readModel.TryProcess(t.e, dbFactory, t.id, null, logger, cancellationToken);
          _ = Task.Run(
            async () =>
            {
              while (processTask is not { IsCompleted: true })
              {
                await TryLockReadModel(readModel.TableName);
                await Task.Delay(TimeSpan.FromSeconds(RefreshStreamLockFrequencySeconds), cancellationToken);
              }
            },
            cancellationToken);
          await processTask;
          PrometheusMetrics.RecordReadModelProcessingTime(
            readModel.TableName,
            (DateTime.UtcNow - candidate.CreatedAt).Milliseconds);

          // Verify foreign model hashes have caught up before marking complete
          foreach (var other in lockedByOtherHash)
          {
            var otherPosition = await GetStreamLastHydratedPositionByHash(other.ModelHash, other.ReadModelName);
            if (otherPosition is null || otherPosition.Value < candidate.Position)
            {
              // Foreign deployment hasn't caught up - don't mark as hydrated
              return null;
            }
          }
        }
      }

      return maybeEntity.Match(t => t.e.Position, () => null);
    }
  }

  #endregion

  #region Queue State Management

  /// <summary>
  /// Updates the queue entry after hydration, clearing the lock and recording progress.
  /// If the update fails (e.g., position changed), just releases the lock.
  /// </summary>
  private async Task MarkAsHydrated(HydrationQueueEntry entry)
  {
    try
    {
      await using var connection = new SqlConnection(connectionString);
      var rowsAffected = await connection.ExecuteAsync(
        UpdateHydrationState,
        new
        {
          entry.StreamName, ModelHash = modelHash, WorkerId = workerId, entry.Position, entry.LastHydratedPosition
        });
      if (rowsAffected == 0)
      {
        // Entry was modified (new event arrived) - just release lock
        await connection.ExecuteAsync(
          ReleaseSql,
          new { entry.StreamName, ModelHash = modelHash, WorkerId = workerId });
      }
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      await MarkAsHydrated(entry);
    }
  }

  /// <summary>
  /// Extends the lock on a stream we're currently processing.
  /// </summary>
  /// <returns>True if lock was refreshed, false if we lost it.</returns>
  private async Task<bool> TryRefreshLock(string streamName)
  {
    try
    {
      await using var connection = new SqlConnection(connectionString);
      var rowsUpdated = await connection.ExecuteAsync(
        TryRefreshStreamLockSql,
        new { WorkerId = workerId, streamName, ModelHash = modelHash });
      return rowsUpdated > 0;
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      return await TryRefreshLock(streamName);
    }
  }

  #endregion

  #region Static Query Methods

  /// <summary>
  /// Gets the total count of entries pending hydration.
  /// </summary>
  public static async Task<int> PendingEventsCount(string modelHash, string connectionString)
  {
    try
    {
      await using var connection = new SqlConnection(connectionString);
      return await connection.QuerySingleAsync<int>(PendingEventsCountSql, new { ModelHash = modelHash });
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      return await PendingEventsCount(modelHash, connectionString);
    }
  }

  /// <summary>
  /// Gets the count of entries pending hydration up to a specific position.
  /// Used by TodoProcessor to verify read models are caught up before executing tasks.
  /// </summary>
  public static async Task<int> PendingEventsCount(string modelHash, string connectionString, ulong position)
  {
    try
    {
      await using var connection = new SqlConnection(connectionString);
      return await connection.QuerySingleAsync<int>(
        PendingEventsBeforePositionCountSql,
        new { ModelHash = modelHash, Position = Convert.ToDecimal(position) });
    }
    catch (SqlException ex) when (ex.Number == 1205) // Deadlock - retry
    {
      await Task.Delay(Random.Shared.Next(150));
      return await PendingEventsCount(modelHash, connectionString, position);
    }
  }

  #endregion

  /// <summary>
  /// Represents a read model lock owned by another model hash (deployment instance).
  /// </summary>
  private record ForeignReadModelLock(string ModelHash, string ReadModelName);
}

/// <summary>
/// Represents an entry in the HydrationQueue table, tracking a stream that needs
/// read model updates and its distributed locking state.
/// </summary>
/// <param name="StreamName">The event stream name (e.g., "order-12345").</param>
/// <param name="SerializedId">JSON-serialized strong ID for deserializing the entity ID.</param>
/// <param name="IdTypeName">Type name of the strong ID class.</param>
/// <param name="IdTypeNamespace">Namespace of the strong ID class.</param>
/// <param name="ModelHash">Schema version identifier of the owning deployment.</param>
/// <param name="Position">Global event store position that triggered this entry.</param>
/// <param name="WorkerId">GUID of the worker currently processing this entry, null if unlocked.</param>
/// <param name="LockedUntil">When the current lock expires, null if unlocked.</param>
/// <param name="TimesLocked">Number of processing attempts. Entries are abandoned at 25.</param>
/// <param name="CreatedAt">When this entry was first created.</param>
/// <param name="IsDynamicConsistencyBoundary">Whether this came from a DCB interest event.</param>
/// <param name="LastHydratedPosition">Last position successfully hydrated, null if never hydrated.</param>
public record HydrationQueueEntry(
  string StreamName,
  string SerializedId,
  string IdTypeName,
  string? IdTypeNamespace,
  string ModelHash,
  decimal Position,
  Guid? WorkerId,
  DateTime? LockedUntil,
  int TimesLocked,
  DateTime CreatedAt,
  bool IsDynamicConsistencyBoundary,
  decimal? LastHydratedPosition);
