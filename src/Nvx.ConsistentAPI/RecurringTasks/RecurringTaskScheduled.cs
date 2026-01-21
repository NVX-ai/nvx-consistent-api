namespace Nvx.ConsistentAPI.RecurringTasks;

public record RecurringTaskScheduled(string Id, DateTime ScheduledAt) : EventModelEvent
{
  public string GetStreamName() => RecurringTaskExecution.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}
