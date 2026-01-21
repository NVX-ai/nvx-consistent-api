using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI.RecurringTasks;

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
