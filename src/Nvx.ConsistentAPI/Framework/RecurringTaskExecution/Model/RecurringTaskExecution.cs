namespace Nvx.ConsistentAPI;

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


