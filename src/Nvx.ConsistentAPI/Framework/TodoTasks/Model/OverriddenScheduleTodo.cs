namespace Nvx.ConsistentAPI;

public interface OverriddenScheduleTodo : TodoData
{
  public DateTime ScheduledAt { get; }
}
