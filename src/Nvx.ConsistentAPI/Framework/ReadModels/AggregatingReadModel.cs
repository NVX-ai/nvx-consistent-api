using System.Data.Common;
using KurrentDB.Client;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.InternalTooling;
using EventTypeFilter = EventStore.Client.EventTypeFilter;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Nvx.ConsistentAPI;

public class AggregatingReadModelDefinition<Shape> : EventModelingReadModelArtifact where Shape : EventModelReadModel
{
  private readonly EtagHolder holder = new();
  private ulong? currentCheckpointPosition;

  private bool isIdle = true;
  private ulong? lastProcessedEventPosition;
  private Func<Task<Unit>> reset = () => unit.ToTask();
  public required ReadModelAggregator[] Aggregators { get; init; }
  public required string[] StreamPrefixes { get; init; }
  public bool IsExposed { get; init; } = true;
  public Type[] AccessoryTables { get; init; } = [];
  public Action<OpenApiOperation> OpenApiCustomizer { get; init; } = _ => { };
  private CancellationTokenSource SubCancelSource { get; set; } = new();
  public bool IsIdempotent { get; init; } = false;
  public required string AreaTag { private get; init; }
  public BuildCustomFilter CustomFilterBuilder { get; init; } = (_, _, _) => new CustomFilter(null, [], null);
  public ReadModelDefaulter<Shape> Defaulter { get; init; } = (_, _, _) => None;
  private ReadModelSyncState SyncState { get; set; } = new(FromAll.Start, DateTime.MinValue, false, false);

  public async Task<SingleReadModelInsights> Insights(ulong lastEventPosition, KurrentDBClient eventStoreClient)
  {
    var currentPosition = lastProcessedEventPosition ?? currentCheckpointPosition ?? 0UL;
    var effectivePosition = lastEventPosition;
    var hadEvents = false;
    if (currentPosition < effectivePosition)
    {
      var prefixFilter = EventTypeFilter.Prefix(StreamPrefixes);
      await foreach (var msg in eventStoreClient.ReadAllAsync(Direction.Backwards, Position.End, prefixFilter).Take(1))
      {
        effectivePosition = msg.Event.Position.CommitPosition;
        hadEvents = true;
      }
    }

    var percentageComplete = effectivePosition == 0 || !hadEvents
      ? 100m
      : Convert.ToDecimal(currentPosition) * 100m / Convert.ToDecimal(effectivePosition);
    var clampedPercentage = Math.Min(100, percentageComplete);
    return
      new SingleReadModelInsights(
        DatabaseHandler<Shape>.TableName(typeof(Shape)),
        lastProcessedEventPosition,
        currentCheckpointPosition,
        true,
        SyncState.HasReachedEndOnce,
        SyncState.LastSync,
        clampedPercentage);
  }

  public async Task ApplyTo(
    WebApplication app,
    KurrentDBClient esClient,
    Fetcher fetcher,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    Emitter emitter,
    GeneratorSettings settings,
    ILogger logger,
    string modelHash)
  {
    var factory = new DatabaseHandlerFactory(settings.ReadModelConnectionString, logger);
    var handler = factory.Get<Shape>();
    if (settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration))
    {
      await handler.Initialize();
      var additionalTables = new Dictionary<Type, AdditionalTableDetails>();

      foreach (var table in AccessoryTables)
      {
        var handlerConstructor = typeof(DatabaseHandler<>)
          .MakeGenericType(table)
          .GetConstructors()
          .First(c =>
            c.GetParameters().Length == 2
            && c.GetParameters()[0].ParameterType == typeof(string)
            && c.GetParameters()[1].ParameterType == typeof(ILogger));
        var accessoryHandler = (DatabaseHandler)handlerConstructor
          .Invoke([settings.ReadModelConnectionString, logger]);
        await accessoryHandler.Initialize();
        additionalTables.Add(
          table,
          new AdditionalTableDetails(
            accessoryHandler.GetTableName(),
            accessoryHandler.UpsertSql,
            accessoryHandler.GenerateSafeInsertSql(),
            accessoryHandler.GenerateUpdateSql(),
            accessoryHandler.AllColumns,
            accessoryHandler.AllColumnsTablePrefixed));
      }

      var tableDetails = new TableDetails(
        handler.GetTableName(),
        handler.UpsertSql,
        handler.TraceableUpsertSql,
        handler.GenerateSafeInsertSql(),
        handler.GenerateUpdateSql(),
        handler.AllColumns,
        additionalTables,
        handler.AllColumnsTablePrefixed);

      _ = Task.Run(() => SubscribeToStream(
        esClient,
        fetcher,
        parser,
        handler,
        settings.ReadModelConnectionString,
        tableDetails,
        logger));
    }

    ReadModelRouteBuilder
      .Apply(
        fetcher,
        handler,
        emitter,
        settings,
        Auth,
        app,
        holder,
        OpenApiCustomizer,
        AreaTag,
        async () => await reset(),
        IsExposed,
        (user, id) => CustomFilterBuilder(user, id, factory),
        Defaulter,
        logger);
  }

  public Type ShapeType { get; } = typeof(Shape);

  public AuthOptions Auth { get; init; } = new Everyone();

  public bool IsUpToDate(ulong? position)
  {
    if (SyncState.IsBeingHydratedByAnotherInstance)
    {
      return true;
    }

    if (position is null)
    {
      return SyncState.HasReachedEndOnce && !HasProcessedRecently();
    }

    if (FromAll.After(new Position(position.Value, position.Value)) <= SyncState.LastPosition)
    {
      return true;
    }

    return SyncState.LastSync < DateTime.UtcNow.AddSeconds(5);
  }

  private bool HasProcessedRecently() => !isIdle || SyncState.LastSync > DateTime.UtcNow.AddMilliseconds(-1_000);

  private async Task SubscribeToStream(
    KurrentDBClient client,
    Fetcher fetcher,
    Func<ResolvedEvent, Option<EventModelEvent>> parser,
    DatabaseHandler<Shape> databaseHandler,
    string connectionString,
    TableDetails tableDetails,
    ILogger logger)
  {
    var syncDelay = Random.Shared.Next(300, 600);
    var prefixFilter = StreamFilter.Prefix(StreamPrefixes);
    var filterOptions = new SubscriptionFilterOptions(prefixFilter);
    reset = async () =>
    {
      isIdle = false;
      await SubCancelSource.CancelAsync();
      await databaseHandler.Reset();
      return unit;
    };
    var processId = Guid.NewGuid();
    HydrationCountTracker? hydrationCountTracker = null;

    while (true)
    {
      StartTracker();
      // ReSharper disable once ExplicitCallerInfoArgument
      var activity = PrometheusMetrics.Source.StartActivity("ReadModelHydration");
      activity?.SetTag("read-model.hydration.name", ShapeType.Name);
      activity?.SetTag("read-model.hydration.kind", "aggregating");
      try
      {
        if (!IsIdempotent && !await databaseHandler.TryAcquireLock(processId, SubCancelSource.Token))
        {
          SyncState = SyncState with { IsBeingHydratedByAnotherInstance = true };
          logger.LogInformation("Another instance of the API is currently hydrating {Name}", ShapeType.Name);
          await Task.Delay(Random.Shared.Next(500, 2_500), SubCancelSource.Token);
          continue;
        }

        var checkpointPosition = await databaseHandler.Checkpoint();
        currentCheckpointPosition = checkpointPosition == Position.Start ? 0 : checkpointPosition.CommitPosition;
        var checkpoint = checkpointPosition == Position.Start ? FromAll.Start : FromAll.After(checkpointPosition);

        SyncState = new ReadModelSyncState(checkpoint, DateTime.UtcNow, checkpoint != FromAll.Start, false);
        await using var subscription = client
          .SubscribeToAll(checkpoint, filterOptions: filterOptions, cancellationToken: SubCancelSource.Token);

        await foreach (var message in subscription.Messages)
        {
          if (!IsIdempotent && !await databaseHandler.TryRefreshLock(processId, SubCancelSource.Token))
          {
            await SubCancelSource.CancelAsync();
            continue;
          }

          switch (message)
          {
            case StreamMessage.Event(var evt):
            {
              PrometheusMetrics.AddReadModelEventsProcessed(ShapeType.Name);
              var parsed = parser(evt);
              var relevantAggregators = Aggregators.Where(a => a.Processes(parsed)).ToArray();
              var canBeAggregated = relevantAggregators.Length != 0;
              isIdle = false;
              await parsed
                .Async()
                .Iter(async e =>
                {
                  await using var connection = new SqlConnection(connectionString);
                  await connection.OpenAsync(SubCancelSource.Token);
                  await using var transaction = await connection.BeginTransactionAsync(SubCancelSource.Token);
                  try
                  {
                    var metadata = EventMetadata.TryParse(evt);
                    var aggregatedEvent =
                      new EventWithMetadata<EventModelEvent>(e, evt.Event.Position, evt.Event.EventId, metadata);

                    var ids = new List<string>();

                    foreach (var aggregator in relevantAggregators)
                    {
                      ids.AddRange(
                        await aggregator.Aggregate(
                          aggregatedEvent,
                          fetcher,
                          connection,
                          transaction,
                          tableDetails));
                    }

                    if (canBeAggregated)
                    {
                      holder.Etag = IdempotentUuid.Generate(evt.Event.Position.ToString()).ToString();
                    }

                    await databaseHandler.UpdateCheckpoint(connection, evt.Event.Position.ToString(), transaction);
                    lastProcessedEventPosition = evt.Event.Position.CommitPosition;
                    await transaction.CommitAsync(SubCancelSource.Token);

                    await ids
                      .Distinct()
                      .Select<string, Func<Task<Unit>>>(id =>
                        async () => await databaseHandler.UpdateArrayColumnsFor(id))
                      .Parallel();
                  }
                  catch (OperationCanceledException)
                  {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                  }
                });
              SyncState = SyncState with
              {
                LastPosition = FromAll.After(evt.OriginalEvent.Position), LastSync = DateTime.UtcNow
              };
              isIdle = true;
              break;
            }
            case StreamMessage.AllStreamCheckpointReached(var pos):
            {
              if (SyncState.LastSync < DateTime.UtcNow.AddSeconds(-syncDelay))
              {
                await databaseHandler.UpdateCheckpoint(FromAll.After(pos).ToString());
                SyncState = SyncState with { LastPosition = FromAll.After(pos), LastSync = DateTime.UtcNow };
                lastProcessedEventPosition = pos.CommitPosition;
                currentCheckpointPosition = pos.CommitPosition;
              }

              isIdle = true;
              break;
            }
            case StreamMessage.CaughtUp:
            {
              SyncState = SyncState with { HasReachedEndOnce = true, LastSync = DateTime.UtcNow };
              ClearTracker();
              activity?.Dispose();
              isIdle = true;
              break;
            }
            case StreamMessage.FellBehind:
            {
              StartTracker();
              break;
            }
          }
        }
      }
      catch (OperationCanceledException)
      {
        SubCancelSource = new CancellationTokenSource();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Error handling {ShapeType} update", ShapeType);
      }
      finally
      {
        activity?.Dispose();
        ClearTracker();
        await Task.Delay(250);
      }
    }
    // ReSharper disable once FunctionNeverReturns

    void ClearTracker()
    {
      hydrationCountTracker?.Dispose();
      hydrationCountTracker = null;
    }

    void StartTracker()
    {
      ClearTracker();
      hydrationCountTracker = new HydrationCountTracker();
    }
  }

  public override int GetHashCode() => Naming.ToSpinalCase<Shape>().GetHashCode();
}

public interface ReadModelAggregator
{
  Task<string[]> Aggregate(
    EventWithMetadata<EventModelEvent> evt,
    Fetcher fetcher,
    DbConnection dbConnection,
    DbTransaction dbTransaction,
    TableDetails tableDetails);

  bool Processes(Option<EventModelEvent> evt);
}

public abstract class ReadModelAggregator<E> : ReadModelAggregator where E : EventModelEvent
{
  public Task<string[]> Aggregate(
    EventWithMetadata<EventModelEvent> evt,
    Fetcher fetcher,
    DbConnection dbConnection,
    DbTransaction dbTransaction,
    TableDetails tableDetails)
  {
    if (evt.Event is E)
    {
      var agg = Aggregate(evt.As((E)evt.Event), fetcher, dbConnection, dbTransaction, tableDetails);
      PrometheusMetrics.RecordAggregatingProcessingTime(tableDetails.TableName, (DateTime.UtcNow - evt.Metadata.CreatedAt).Milliseconds);
      return agg;
    }
    return Task.FromResult<string[]>([]);
  }

  public bool Processes(Option<EventModelEvent> evt) => evt.Map(e => e is E).DefaultValue(false);

  protected abstract Task<string[]> Aggregate(
    EventWithMetadata<E> evt,
    Fetcher fetcher,
    DbConnection dbConnection,
    DbTransaction dbTransaction,
    TableDetails tableDetails);
}

public record TableDetails(
  string TableName,
  string UpsertSql,
  string TraceableUpsertSql,
  string SafeInsertSingleSql,
  string UpdateSingleSql,
  string AllColumns,
  IReadOnlyDictionary<Type, AdditionalTableDetails> AdditionalTables,
  string AllColumnsTablePrefixed);

public record AdditionalTableDetails(
  string TableName,
  string UpsertSql,
  string SafeInsertSingleSql,
  string UpdateSingleSql,
  string AllColumns,
  string AllColumnsTablePrefixed);
