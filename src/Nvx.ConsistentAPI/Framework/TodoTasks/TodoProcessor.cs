using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nvx.ConsistentAPI.InternalTooling;
using Nvx.ConsistentAPI.Metrics;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Nvx.ConsistentAPI;

internal class TodoProcessor
{
  private static readonly Type UserWithTenantPermissionIdType = typeof(UserWithTenantPermissionId);
  private static readonly Type UserWithPermissionIdType = typeof(UserWithPermissionId);
  private static readonly Type StrongStringType = typeof(StrongString);
  private static readonly Type StrongGuidType = typeof(StrongGuid);
  private readonly SemaphoreSlim runningTodosSemaphore;
  
  private const int BatchSize = 45;
  private readonly Lock _lock = new();
  private static int _padding;
  
  private readonly List<RunningTodoTaskInsight> runningTodoTasks = [];
  private readonly List<TodoEventModelReadModel> todoTaskQueue = [];
  private readonly List<TodoEventModelReadModel> todoTaskRunning = [];

  private readonly string tableName =
    DatabaseHandler<TodoEventModelReadModel>.TableName(typeof(TodoEventModelReadModel));

  public TodoProcessor()
  {
    var todoProcessorWorkerCount = Settings?.TodoProcessorWorkerCount ?? 25;
    runningTodosSemaphore = new SemaphoreSlim(todoProcessorWorkerCount, todoProcessorWorkerCount);
  }

  public required GeneratorSettings Settings { private get; init; }
  public required ILogger Logger { private get; init; }
  public required Fetcher Fetcher { private get; init; }
  public required Emitter Emitter { private get; init; }
  public required TodoTaskDefinition[] Tasks { private get; init; }
  public required ReadModelHydrationDaemon HydrationDaemon { private get; init; }
  internal required EventModelingReadModelArtifact[] ReadModels { get; init; }

  internal async Task<RunningTodoTaskInsight[]> GetRunningTodoTasks()
  {
    await runningTodosSemaphore.WaitAsync();
    var currentlyRunning = runningTodoTasks
      .GroupBy(rtt => rtt.TaskType)
      .Select(g => new RunningTodoTaskInsight(g.Key, g.SelectMany(rtt => rtt.RelatedEntityIds).ToArray()))
      .ToArray();
    runningTodosSemaphore.Release();
    return currentlyRunning;
  }

  internal async Task<RunningTodoTaskInsight[]> AboutToRunTasks()
  {
    await runningTodosSemaphore.WaitAsync();
    var currentlyRunning = runningTodoTasks.ToArray();
    runningTodosSemaphore.Release();
    return await GetAboutToRunTodos()
      .Map(ts => ts
        .Choose(todoReadModel =>
          Tasks
            .FirstOrNone(t => t.Type == todoReadModel.Name)
            .Map(todoTaskDefinition => (todoTaskDefinition, todoReadModel))
        )
        .Where(t => currentlyRunning.All(rt =>
          rt.TaskType != t.todoTaskDefinition.Type
          && rt.RelatedEntityIds.All(id => id != t.todoReadModel.RelatedEntityId)))
        .GroupBy(t => t.todoTaskDefinition.Type)
        .Select(g => new RunningTodoTaskInsight(g.Key, g.Select(t => t.todoReadModel.RelatedEntityId).ToArray()))
        .ToArray());
  }

  internal void Initialize()
  {
    for (var i = 0; i < Settings.TodoProcessorWorkerCount; i++)
    {
      var index = i;
      RunPeriodically(async () => { await ProcessAsWorker(index); });
    }
  }

  private async Task ProcessAsWorker(int index)
  {
    RunningTodoTaskInsight? insight = null;
    try
    {
      await runningTodosSemaphore.WaitAsync();
      var todo = await TryGetNextAvailableTodo()
        .Async()
        .Bind(todoReadModel =>
          Tasks
            .FirstOrNone(t => t.Type == todoReadModel.Name)
            .Map(todoTaskDefinition => (todoTaskDefinition, todoReadModel)));
      
      Interlocked.Add(ref _padding, 100);
      
      await todo
        .Match(
          async selectedTodo =>
          {
            using var _ = new BatchTodoCountTracker(1);
            runningTodoTasks.Add(
              insight =
                new RunningTodoTaskInsight(
                  selectedTodo.todoTaskDefinition.Type,
                  [selectedTodo.todoReadModel.RelatedEntityId]));
            Logger.LogInformation("Worker {WorkerIndex} picked up todo {Todo} with id {id}", index, selectedTodo.todoTaskDefinition.Type, selectedTodo.todoReadModel.Id);
            await ProcessOne(selectedTodo)();
          },
          async () => { await Task.Delay(Random.Shared.Next((index + 1) * 500)); });
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed processing todos (worker)");
    }
    finally
    {
      if (insight is not null)
      {
        runningTodoTasks.RemoveAll(rtt => rtt.TaskType == insight.TaskType
                                          && insight.RelatedEntityIds.All(id => rtt.RelatedEntityIds.Contains(id)));
      }
      runningTodosSemaphore.Release();
    }
  }

  private Func<Task<Unit>> ProcessOne((TodoTaskDefinition definition, TodoEventModelReadModel todo) t) =>
    async () =>
    {
      using var activity = PrometheusMetrics.Source.StartActivity(nameof(TodoProcessor));
      try
      {
        // Await for all relevant read models to be up-to-date.
        if (t.definition.DependingReadModels.All(_ => ReadModels.All(rm => rm.IsUpToDate(t.todo.EventPosition)))
            && await HydrationDaemon.IsUpToDate(t.todo.EventPosition))
        {
          return await TryFetch()
            .Option
            .Bind(fetchResult => fetchResult.Ent.Map(entity => (entity, fetchResult.Revision)))
            .Iter(async tuple =>
              await RequestLock(tuple.entity, tuple.Revision)
                .Bind(_ =>
                {
                  PrometheusMetrics.AddRunnningTodoCount(t.todo.Name);
                  return Emitter.Emit(Decider).Async().Map(_ => unit);
                })
                .Match(_ => Complete(), e => e.Match(ErrorApi, Outcome))
            );
        }

        return await TryFetch()
          .Match(fr => fr.Ent, _ => None)
          .Async()
          .Bind(e => e.LockState.Match(_ => Some(unit), _ => None))
          .Iter(_ =>
            Emitter
              .Emit(() =>
              {
                Logger.LogWarning("Todo {Todo} is waiting for read models to be up-to-date", t.todo);
                try
                {
                  // ReSharper disable once AccessToDisposedClosure
                  activity?.SetTag("todo.result", "waiting-read-models");
                }
                catch
                {
                  // ignore
                }

                return new AnyState(new TodoHadDependingReadModelBehind(Guid.Parse(t.todo.Id)));
              })
              .Async()
              .Match(_ => unit, _ => unit));
      }
      catch (Exception ex)
      {
        activity?.SetTag("todo.result", "failure");
        if (t.todo.RetryCount == ProcessorEntity.MaxAttempts - 1)
        {
          PrometheusMetrics.AddFailedTodoCount(t.todo.Name);
          Logger.LogCritical(
            ex,
            @"Failed processing todo:\n{Todo}\nFor task {TaskType} after {RetryLimit} retries, it will not run again",
            t.todo,
            t.definition.Type,
            ProcessorEntity.MaxAttempts - 1);
        }
        else
        {
          PrometheusMetrics.AddFailedRetryTodoCount(t.todo.Name);
          Logger.LogError(
            ex,
            @"Failed processing todo:\n{Todo}\nFor task {TaskType}",
            t.todo,
            t.definition.Type);
        }
      }

      return unit;

      AsyncResult<T, Du<ApiError, TodoOutcome>> Elevate<T>(AsyncResult<T, TodoOutcome> ar) =>
        ar.MapError(Second<ApiError, TodoOutcome>);

      AsyncResult<Unit, Du<ApiError, TodoOutcome>> RequestLock(ProcessorEntity p, long revision) =>
        from available in p.LockState.Apply(Elevate)
        from locked in TryInsertLock(revision, available)
        select locked;

      AsyncResult<FetchResult<ProcessorEntity>, Du<ApiError, TodoOutcome>> TryFetch() =>
        Fetcher.Fetch<ProcessorEntity>(Some(t.todo.GetStrongId()));

      AsyncResult<Unit, Du<ApiError, TodoOutcome>> TryInsertLock(
        long revision,
        // ReSharper disable once UnusedParameter.Local
        // This is to ensure that this is called only after checking that locking is possible.
        ProcessorEntity.LockAvailable _
      ) =>
        Emitter
          .TryInsert(
            new TodoLockRequested(
              t.todo.Id.Apply(Guid.Parse),
              DateTime.UtcNow,
              t.definition.LockLength
            ),
            revision,
            null
          )
          .MapError(_ => TodoOutcome.Locked)
          .Apply(Elevate);

      async Task<Unit> Complete()
      {
        var now = DateTime.UtcNow;
        var result = await Emitter
          .Emit(() => new AnyState(new TodoCompleted(t.todo.Id.Apply(Guid.Parse), now)))
          .Async()
          .Match(_ => unit, _ => unit);
        PrometheusMetrics.AddCompletedTodoCount(t.todo.Name);
        return result;
      }

      Task<Unit> Release() =>
        Emitter
          .Emit(() => new AnyState(new TodoLockReleased(t.todo.Id.Apply(Guid.Parse))))
          .Async()
          .Match(_ => unit, _ => unit);

      // This will retry, but not release the lock, adding some effective delay.
      Task<Unit> ErrorApi(ApiError ae) => Task.FromResult(unit);

      Task<Unit> Outcome(TodoOutcome outcome) =>
        outcome switch
        {
          TodoOutcome.Locked => unit.ToTask(),
          TodoOutcome.Done => Complete(),
          _ => Release()
        };

      AsyncResult<EventInsertion, Du<ApiError, TodoOutcome>> Decider()
      {
        return Go();

        async Task<Result<EventInsertion, Du<ApiError, TodoOutcome>>> Go()
        {
          // ReSharper disable once AccessToDisposedClosure
          activity?.SetTag("todo.id", t.todo.SerializedRelatedEntityId ?? t.todo.Id);
          // ReSharper disable once AccessToDisposedClosure
          activity?.SetTag("todo.name", t.todo.Name);

          var stopwatch = Stopwatch.StartNew();
          var result = await t.definition
            .Execute(
              t.todo.JsonData,
              Fetcher,
              GetStrongId(),
              Settings.ReadModelConnectionString,
              Logger);
          stopwatch.Stop();
          PrometheusMetrics.RecordTodoProcessingTime(t.todo.Name, stopwatch.Elapsed.TotalMilliseconds);

          result.Iter(
            // ReSharper disable once AccessToDisposedClosure
            _ => activity?.SetTag("todo.result", "success"),
            // ReSharper disable once AccessToDisposedClosure
            r => activity?.SetTag(
              "todo.result",
              r switch
              {
                TodoOutcome.Done => "success",
                TodoOutcome.Locked => "locked",
                TodoOutcome.Retry => "retry",
                _ => "unknown"
              }));

          return result.Match(
            Ok<EventInsertion, Du<ApiError, TodoOutcome>>,
            to => to.Apply(Second<ApiError, TodoOutcome>)
          );

          StrongId GetStrongId()
          {
            if (!string.IsNullOrEmpty(t.todo.SerializedRelatedEntityId))
            {
              try
              {
                return (StrongId)JsonConvert.DeserializeObject(
                  t.todo.SerializedRelatedEntityId!,
                  t.definition.EntityIdType)!;
              }
              catch (Exception ex)
              {
                Logger.LogError(
                  ex,
                  "Failed deserializing {SerializedRelatedEntityId}\nfor task {Todo}\nfor definition {Definition}",
                  t.todo.SerializedRelatedEntityId,
                  t.todo,
                  t.definition);
                return new StrongString(t.todo.RelatedEntityId);
              }
            }

            if (StrongGuidType == t.definition.EntityIdType)
            {
              return new StrongGuid(Guid.TryParse(t.todo.RelatedEntityId, out var id) ? id : Guid.NewGuid());
            }

            if (StrongStringType == t.definition.EntityIdType)
            {
              return new StrongString(t.todo.RelatedEntityId);
            }

            if (UserWithPermissionIdType == t.definition.EntityIdType)
            {
              try
              {
                return new UserWithPermissionId(
                  t.todo.RelatedEntityId.Split("#")[0],
                  t.todo.RelatedEntityId.Split("#")[1]);
              }
              catch (Exception ex)
              {
                Logger.LogError(
                  ex,
                  "Failed deserializing UserWithPermissionId: {RelatedEntityId}\nfor task {Todo}\nfor definition {Definition}",
                  t.todo.RelatedEntityId,
                  t.todo,
                  t.definition);
                return new StrongString(t.todo.RelatedEntityId);
              }
            }

            if (UserWithTenantPermissionIdType == t.definition.EntityIdType)
            {
              try
              {
                return new UserWithTenantPermissionId(
                  t.todo.RelatedEntityId.Split("#")[0],
                  Guid.Parse(t.todo.RelatedEntityId.Split("#")[1]),
                  t.todo.RelatedEntityId.Split("#")[2]
                );
              }
              catch (Exception ex)
              {
                Logger.LogError(
                  ex,
                  "Failed deserializing UserWithTenantPermissionId: {RelatedEntityId}\nfor task {Todo}\nfor definition {Definition}",
                  t.todo.RelatedEntityId,
                  t.todo,
                  t.definition);
              }
            }

            return new StrongString(t.todo.RelatedEntityId);
          }
        }
      }
    };

  private async Task<IEnumerable<TodoEventModelReadModel>> GetAboutToRunTodos()
  {
    try
    {
      var aMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
      var now = DateTime.UtcNow;

      var query =
        $"""
         SELECT TOP (@BatchSize)
             [Id],
             [RelatedEntityId],
             [StartsAt],
             [ExpiresAt],
             [CompletedAt],
             [JsonData],
             [Name],
             [LockedUntil],
             [SerializedRelatedEntityId],
             [EventPosition],
             [RetryCount],
             [IsFailed]
         FROM [{tableName}]
         WHERE
             ([StartsAt] <= @aMinuteAgo)
             AND [ExpiresAt] > @now
             AND ([LockedUntil] IS NULL OR [LockedUntil] < @aMinuteAgo)
             AND [IsFailed] = 0
             AND [CompletedAt] IS NULL
         ORDER BY [StartsAt] ASC
         """;

      await using var connection = new SqlConnection(Settings.ReadModelConnectionString);
      return await connection.QueryAsync<TodoEventModelReadModel>(
        query,
        new { BatchSize = BatchSize, aMinuteAgo, now });
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed getting about to run todos");
      throw;
    }
  }
  
  private async Task<Option<TodoEventModelReadModel>> TryGetNextAvailableTodo()
  {
    lock (_lock)
    {
      if (todoTaskQueue.Count > 0)
      {
        var todo = todoTaskQueue.First();
        todoTaskRunning.Add(todo);
        todoTaskQueue.RemoveAt(0);
        return todo;
      }
    }

    var getNextAvailable = await GetNextAvailableTodo();
    
    lock (_lock)
    {
      // add only new items to the locked list, to avoid locking more than necessary in case of multiple workers
      var newItems = getNextAvailable
        .Where(r => todoTaskRunning.All(ttr => ttr.Id != r.Id))
        .Where(r => todoTaskQueue.All(ltt => ltt.Id != r.Id))
        .ToList();
      
      if (newItems.Count == 0)
      {
        return None;
      }
      
      Logger.LogInformation("Queued {Count} new todos for processing", newItems.Count);

      var newTodo = newItems.First();
      todoTaskRunning.AddRange(newTodo);
      todoTaskQueue.AddRange(newItems.Skip(1));

      return newTodo;
    }
  }

  private async Task<List<TodoEventModelReadModel>> GetNextAvailableTodo()
  {
    try
    {
      // If cache is empty, get from database and add to cache
      var now = DateTime.UtcNow;
      var query =
        $"""
          SELECT TOP (@BatchSize)
              [Id],
              [RelatedEntityId],
              [StartsAt],
              [ExpiresAt],
         [CompletedAt],
              [JsonData],
              [Name],
              [LockedUntil],
              [SerializedRelatedEntityId],
              [EventPosition],
              [RetryCount],
              [IsFailed]
          FROM [{tableName}]
          WHERE
              [StartsAt] <= @now
              AND [ExpiresAt] > @now
              AND ([LockedUntil] IS NULL OR [LockedUntil] < @now)
              AND [IsFailed] = 0
              AND [CompletedAt] IS NULL
          ORDER BY [StartsAt] ASC
         """;

      await using var connection = new SqlConnection(Settings.ReadModelConnectionString);
      var queryResult = await connection.QueryAsync<TodoEventModelReadModel>(
        query,
        new { BatchSize = BatchSize, now });
      return queryResult.AsList();
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed getting available todos from read model {TableName}", tableName);
      throw;
    }
  }

  private void RunPeriodically(Func<Task> taskFunc) =>
    Task.Run(async () =>
    {
      while (Settings.EnabledFeatures.HasFlag(FrameworkFeatures.Tasks))
      {
        try
        {
          await Task.Delay(500);
          await taskFunc();
        }
        catch (Exception ex)
        {
          Logger.LogError(ex, "Failure in the task running loop");
        }
      }
    });
}
