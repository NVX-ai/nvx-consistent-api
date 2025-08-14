using System.Data.Common;
using EventStore.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.InternalTooling;
using Nvx.ConsistentAPI.Store.Events;
using Nvx.ConsistentAPI.Store.Store;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Nvx.ConsistentAPI;

public class AggregatingReadModelDefinition<Shape> : EventModelingReadModelArtifact where Shape : EventModelReadModel
{
  private readonly EtagHolder holder = new();
  private ulong? currentCheckpointPosition;

  private bool isCaughtUp;
  private bool isProcessing;
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
  private ReadModelSyncState SyncState { get; set; } = new(0, DateTime.MinValue, false, false);

  public async Task<SingleReadModelInsights> Insights(ulong lastEventPosition, EventStore<EventModelEvent> store)
  {
    var effectivePosition = 0UL;
    await foreach (var solvedEvent in store.Read(ReadAllRequest.End(StreamPrefixes)).Events().Take(1))
    {
      effectivePosition = solvedEvent.Metadata.GlobalPosition;
    }

    var currentPosition = lastProcessedEventPosition ?? currentCheckpointPosition ?? 0UL;
    var percentageComplete = effectivePosition == 0
      ? 100m
      : Convert.ToDecimal(currentPosition) * 100m / Convert.ToDecimal(effectivePosition);
    return new SingleReadModelInsights(
      DatabaseHandler<Shape>.TableName(typeof(Shape)),
      lastProcessedEventPosition,
      currentCheckpointPosition,
      true,
      Math.Min(100, isCaughtUp && !isProcessing ? 100 : percentageComplete));
  }

  public async Task ApplyTo(
    WebApplication app,
    EventStore<EventModelEvent> store,
    Fetcher fetcher,
    Emitter emitter,
    GeneratorSettings settings,
    ILogger logger)
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
        store,
        fetcher,
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
      return SyncState.HasReachedEndOnce && isCaughtUp;
    }

    if (position <= SyncState.LastPosition)
    {
      return true;
    }

    return SyncState.LastSync < DateTime.UtcNow.AddSeconds(5);
  }

  private async Task SubscribeToStream(
    EventStore<EventModelEvent> store,
    Fetcher fetcher,
    DatabaseHandler<Shape> databaseHandler,
    string connectionString,
    TableDetails tableDetails,
    ILogger logger)
  {
    var syncDelay = Random.Shared.Next(300, 600);
    reset = async () =>
    {
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
        currentCheckpointPosition = checkpointPosition ?? 0;
        var checkpoint = checkpointPosition is null
          ? FromAll.Start
          : FromAll.After(new Position(checkpointPosition.Value, checkpointPosition.Value));

        SyncState =
          new ReadModelSyncState(checkpointPosition ?? 0, DateTime.UtcNow, checkpoint != FromAll.Start, false);

        var request = checkpointPosition is null
          ? SubscribeAllRequest.Start(StreamPrefixes)
          : SubscribeAllRequest.After(checkpointPosition.Value, StreamPrefixes);


        await foreach (var message in store.Subscribe(request, SubCancelSource.Token))
        {
          if (!IsIdempotent && !await databaseHandler.TryRefreshLock(processId, SubCancelSource.Token))
          {
            await SubCancelSource.CancelAsync();
            continue;
          }

          switch (message)
          {
            case ReadAllMessage<EventModelEvent>.AllEvent evt:
            {
              var relevantAggregators = Aggregators.Where(a => a.Processes(Some(evt.Event))).ToArray();
              var canBeAggregated = relevantAggregators.Length != 0;
              isProcessing = true;

              await using var connection = new SqlConnection(connectionString);
              await connection.OpenAsync(SubCancelSource.Token);
              await using var transaction = await connection.BeginTransactionAsync(SubCancelSource.Token);
              try
              {
                var metadata = EventMetadata.From(evt.Metadata);
                var aggregatedEvent =
                  new EventWithMetadata<EventModelEvent>(
                    evt.Event,
                    evt.Metadata.GlobalPosition,
                    evt.Metadata.EventId,
                    metadata);

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
                  holder.Etag = IdempotentUuid.Generate(evt.Metadata.GlobalPosition.ToString()).ToString();
                }

                await databaseHandler.UpdateCheckpoint(connection, evt.Metadata.GlobalPosition, transaction);
                lastProcessedEventPosition = evt.Metadata.GlobalPosition;
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

              SyncState = SyncState with
              {
                LastPosition = evt.Metadata.GlobalPosition,
                LastSync = DateTime.UtcNow
              };
              isProcessing = false;
              break;
            }
            case ReadAllMessage<EventModelEvent>.Checkpoint(var pos):
            {
              if (SyncState.LastSync < DateTime.UtcNow.AddSeconds(-syncDelay))
              {
                await databaseHandler.UpdateCheckpoint(pos);
                SyncState = SyncState with { LastPosition = pos, LastSync = DateTime.UtcNow };
                lastProcessedEventPosition = pos;
                currentCheckpointPosition = pos;
              }

              isProcessing = false;
              break;
            }
            case ReadAllMessage<EventModelEvent>.CaughtUp:
            {
              SyncState = SyncState with { HasReachedEndOnce = true };
              ClearTracker();
              activity?.Dispose();
              isCaughtUp = true;
              break;
            }
            case ReadAllMessage<EventModelEvent>.FellBehind:
            {
              StartTracker();
              isCaughtUp = false;
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
        isProcessing = false;
        activity?.Dispose();
        ClearTracker();
        await databaseHandler.ReleaseLock(processId);
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
    TableDetails tableDetails) =>
    evt.Event is E e
      ? Aggregate(evt.As(e), fetcher, dbConnection, dbTransaction, tableDetails)
      : Task.FromResult<string[]>([]);

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
