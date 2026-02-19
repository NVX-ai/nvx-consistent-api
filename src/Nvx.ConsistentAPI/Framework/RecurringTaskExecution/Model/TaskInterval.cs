using static System.DayOfWeek;

namespace Nvx.ConsistentAPI.Model;

public record TaskInterval(TimeOnly[] ScheduledAt, DayOfWeek[] DaysOfWeek)
{
  public static TaskInterval ForAllWeek(TimeOnly[] scheduledAt) =>
    new(scheduledAt, Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().ToArray());

  public static TaskInterval ForWorkWeek(TimeOnly[] scheduledAt) =>
    new(scheduledAt, [Monday, Tuesday, Wednesday, Thursday, Friday]);

  public static TaskInterval ForWeekend(TimeOnly[] scheduledAt) => new(scheduledAt, [Saturday, Sunday]);
}
