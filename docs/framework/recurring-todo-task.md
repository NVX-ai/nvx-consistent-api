# Recurring todo task
Similar to the Todo task, except it is not triggered by an event, but by a schedule.

The recurring task does not have a shape, only a definition, this is an example of two recurring tasks that are triggered by a schedule, saving the start of the work day in the stream, and later, the end of the work day (that is, assuming this is a country that operates in UTC with no daylight savings).

```cs
new RecurringTaskDefinition()
{
  Interval = TaskInterval.ForWorkWeek(new[] { new TimeOnly(8, 0)  }),
  TaskName = "track-work-day-start",
  Action = async (fetcher, dbHandlerFactory) =>
  {
    return new CreateStream(new WorkDayStarted(DateTime.UtcNow.Date));
  }
}

new RecurringTaskDefinition()
{
  Interval = TaskInterval.ForWorkWeek(new[] { new TimeOnly(18, 0)  }),
  TaskName = "track-work-day-end",
  Action = async (fetcher, dbHandlerFactory) =>
  {
    return new CreateStream(new WorkDayEnded(DateTime.UtcNow.Date));
  }
}
```

## Definition
### Interval
The interval at which the task will be executed. It takes a collection of `TimeOnly` values, which represent times at which the task will be executed. The class has this signature:
```cs
public record TaskInterval(TimeOnly[] ScheduledAt, DayOfWeek[] DaysOfWeek);
```
There are factory methods meant to help for the most common scenarios, `ForAllWeek`, `ForWorkWeek`, and `ForWeekend`.
