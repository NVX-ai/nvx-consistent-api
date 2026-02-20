using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.InternalTooling;
using Nvx.ConsistentAPI.Metrics;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Background worker that polls for and processes todo tasks using multiple concurrent workers.
/// Each worker periodically queries the database for available todos, acquires a lock, executes
/// the task definition's action, and emits completion or retry events.
/// </summary>
internal class TodoProcessor
{
  private readonly RunningTodoTracker runningTodoTracker = new();
  private readonly TodoRepository repository;

  public required GeneratorSettings Settings { private get; init; }
  public required ILogger Logger { private get; init; }
  public required Fetcher Fetcher { private get; init; }
  public required Emitter Emitter { private get; init; }
  public required TodoTaskDefinition[] Tasks { private get; init; }
  public required ReadModelHydrationDaemon HydrationDaemon { private get; init; }
  internal required EventModelingReadModelArtifact[] ReadModels { get; init; }

  public TodoProcessor() => repository = null!;

  internal void InitializeRepository() =>
    Unsafe.AsRef(in repository) = new TodoRepository(Settings.ReadModelConnectionString, Logger);

  internal Task<RunningTodoTaskInsight[]> GetRunningTodoTasks() =>
    runningTodoTracker.GetRunningTodoTasks();

  internal async Task<RunningTodoTaskInsight[]> AboutToRunTasks()
  {
    var currentlyRunning = await runningTodoTracker.GetCurrentlyRunning();
    return await repository.GetAboutToRunTodos()
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
    InitializeRepository();
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
      var todo = await repository.GetNextAvailableTodo()
        .Async()
        .Bind(todoReadModel =>
          Tasks
            .FirstOrNone(t => t.Type == todoReadModel.Name)
            .Map(todoTaskDefinition => (todoTaskDefinition, todoReadModel)));

      await todo
        .Match(
          async selectedTodo =>
          {
            using var _ = new BatchTodoCountTracker(1);
            insight = await runningTodoTracker.Add(
              selectedTodo.todoTaskDefinition.Type,
              selectedTodo.todoReadModel.RelatedEntityId);
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
      await runningTodoTracker.Remove(insight);
    }
  }

  private Func<Task<Unit>> ProcessOne((TodoTaskDefinition definition, TodoEventModelReadModel todo) t) =>
    async () =>
    {
      using var activity = PrometheusMetrics.Source.StartActivity(nameof(TodoProcessor));
      try
      {
        if (AreReadModelsUpToDate(t.todo.EventPosition) && await HydrationDaemon.IsUpToDate(t.todo.EventPosition))
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
          .Iter(_ => EmitWaitingForReadModels());
      }
      catch (Exception ex)
      {
        HandleProcessingError(ex, activity);
      }

      return unit;

      bool AreReadModelsUpToDate(ulong? eventPosition) =>
        t.definition.DependingReadModels.All(_ => ReadModels.All(rm => rm.IsUpToDate(eventPosition)));

      Unit EmitWaitingForReadModels() =>
        Emitter
          .Emit(() =>
          {
            Logger.LogWarning("Todo {Todo} is waiting for read models to be up-to-date", t.todo);
            try
            {
              activity?.SetTag("todo.result", "waiting-read-models");
            }
            catch
            {
              // ignore
            }

            return new AnyState(new TodoHadDependingReadModelBehind(Guid.Parse(t.todo.Id)));
          })
          .Async()
          .Match(_ => unit, _ => unit)
          .GetAwaiter()
          .GetResult();

      void HandleProcessingError(Exception ex, Activity? act)
      {
        act?.SetTag("todo.result", "failure");
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
        LockAvailable _
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
          activity?.SetTag("todo.id", t.todo.SerializedRelatedEntityId ?? t.todo.Id);
          activity?.SetTag("todo.name", t.todo.Name);

          var stopwatch = Stopwatch.StartNew();
          var result = await t.definition
            .Execute(
              t.todo.JsonData,
              Fetcher,
              StrongIdResolver.Resolve(t.todo, t.definition, Logger),
              Settings.ReadModelConnectionString,
              Logger);
          stopwatch.Stop();
          PrometheusMetrics.RecordTodoProcessingTime(t.todo.Name, stopwatch.Elapsed.TotalMilliseconds);

          result.Iter(
            _ => activity?.SetTag("todo.result", "success"),
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
        }
      }
    };

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
