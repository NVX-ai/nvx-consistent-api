namespace TestEventModel;

public static class DateTimeConversions
{
  public static DateOnly? ToLocalDateOnly(this DateTimeOffset? self) =>
    self is null ? null : new DateOnly(self.Value.Year, self.Value.Month, self.Value.Day);

  public static DateOnly ToLocalDateOnly(this DateTimeOffset self) => new(self.Year, self.Month, self.Day);
}
