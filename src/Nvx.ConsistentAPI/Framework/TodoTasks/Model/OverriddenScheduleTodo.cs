namespace Nvx.ConsistentAPI;

/// <summary>
/// Extends <see cref="TodoData"/> to allow a todo to specify a custom start time
/// instead of using the default delay from the task definition.
/// </summary>
public interface OverriddenScheduleTodo : TodoData
{
  DateTime ScheduledAt { get; }
}
