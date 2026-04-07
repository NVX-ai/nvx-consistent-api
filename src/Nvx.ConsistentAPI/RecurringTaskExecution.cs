using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI;

using static DayOfWeek;

public partial record RecurringTaskExecution(string Id, DateTime ScheduledAt)
  : EventModelEntity<RecurringTaskExecution>,
    Folds<RecurringTaskScheduled, RecurringTaskExecution>
{
  public const string StreamPrefix = "entity-recurring-task-execution-";

  public static readonly EventModel Get =
    new()
    {
      Entities =
      [
        new EntityDefinition<RecurringTaskExecution, StrongString>
        {
          Defaulter = Defaulted, StreamPrefix = StreamPrefix
        }
      ]
    };

  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<RecurringTaskExecution>
    Fold(RecurringTaskScheduled evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { ScheduledAt = evt.ScheduledAt });

  public static string GetStreamName(string id) => $"{StreamPrefix}{id}";
  public static RecurringTaskExecution Defaulted(StrongString id) => new(id.Value, DateTime.MinValue);
}

public record RecurringTaskScheduled(string Id, DateTime ScheduledAt) : EventModelEvent
{
  public string GetStreamName() => RecurringTaskExecution.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}

public class RecurringTaskDefinition
{
  public required TaskInterval Interval { get; init; }
  public required string TaskName { get; init; }

  public required
    Func<Fetcher, DatabaseHandlerFactory, ILogger, Task<Du<EventInsertion, TodoOutcome>>>
    Action { get; init; }

  public TimeSpan Expiration { get; init; } = TimeSpan.FromDays(7);

  public DateTime NextExecutionDate()
  {
    var now = DateTime.UtcNow.Apply(n => new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second));

    var nextExecutionCandidate = now.AddSeconds(-1);
    var times =
      Interval
        .ScheduledAt
        .Select(sa => new TimeOnly(sa.Hour, sa.Minute, sa.Second))
        .Order()
        .Select(to => to.ToTimeSpan())
        .ToArray();

    while (
      nextExecutionCandidate < now
      || !Interval.DaysOfWeek.Contains(nextExecutionCandidate.DayOfWeek)
      || !times.Contains(nextExecutionCandidate.TimeOfDay)
    )
    {
      var candidateTime = nextExecutionCandidate.TimeOfDay;
      var candidateDate = nextExecutionCandidate.Date;

      nextExecutionCandidate = times
        .Where(t => candidateTime < t)
        .FirstOrNone()
        .Match(
          next => candidateDate.Add(next),
          () => candidateDate.AddDays(1)
        );
    }

    return nextExecutionCandidate;
  }

  internal TodoTaskDefinition ToTodoTaskDefinition() =>
    new TodoTaskDefinition<ScheduledTaskData, RecurringTaskExecution, RecurringTaskScheduled, StrongString>
    {
      Type = TaskName,
      Action = (_, _, fetcher, dbFactory, logger) => Action(fetcher, dbFactory, logger),
      Originator = (evt, _, _) => new ScheduledTaskData(evt.ScheduledAt),
      SourcePrefix = RecurringTaskExecution.StreamPrefix,
      LockLength = TimeSpan.FromMinutes(5),
      Expiration = Expiration
    };

  public override int GetHashCode() => TaskName.GetHashCode();
}

public record ScheduledTaskData(DateTime ScheduledAt) : OverriddenScheduleTodo;

public record TaskInterval(TimeOnly[] ScheduledAt, DayOfWeek[] DaysOfWeek)
{
  public static TaskInterval ForAllWeek(TimeOnly[] scheduledAt) =>
    new(scheduledAt, Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().ToArray());

  public static TaskInterval ForWorkWeek(TimeOnly[] scheduledAt) =>
    new(scheduledAt, [Monday, Tuesday, Wednesday, Thursday, Friday]);

  public static TaskInterval ForWeekend(TimeOnly[] scheduledAt) => new(scheduledAt, [Saturday, Sunday]);
}

public class RecurringTaskRunner
{
  private readonly Dictionary<string, string> recentSchedules = new();

  public void Initialize(
    RecurringTaskDefinition[] definitions,
    Fetcher fetcher,
    Emitter emitter,
    GeneratorSettings settings,
    ILogger logger)
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.Tasks))
    {
      return;
    }

    Task.Run(async () =>
    {
      while (true)
      {
        foreach (var definition in definitions)
        {
          try
          {
            await Run(definition, fetcher, emitter);
          }
          catch (Exception ex)
          {
            logger.LogError(ex, "Failed to run recurring task {TaskName}", definition.TaskName);
          }
        }

        await Task.Delay(500);
      }
      // ReSharper disable once FunctionNeverReturns
    });
  }

  private async Task Run(RecurringTaskDefinition definition, Fetcher fetcher, Emitter emitter)
  {
    var nextDate = definition.NextExecutionDate();
    var executionId = $"{definition.TaskName}-{nextDate:u}";
    if (recentSchedules.TryGetValue(definition.TaskName, out var value) && value == executionId)
    {
      return;
    }

    var execution = await fetcher.Fetch<RecurringTaskExecution>(new StrongString(executionId));
    if (execution.Ent.IsSome)
    {
      recentSchedules[definition.TaskName] = executionId;
      return;
    }

    var result = await emitter.Emit(
      () => new CreateStream(new RecurringTaskScheduled(executionId, nextDate)),
      shouldSkipRetry: true
    );

    if (result.IsOk)
    {
      recentSchedules[definition.TaskName] = executionId;
    }
  }
}
