namespace Nvx.ConsistentAPI.RecurringTasks;

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
