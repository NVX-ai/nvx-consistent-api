namespace Nvx.ConsistentAPI.Tests;

public class DateHandling
{
  [Fact(DisplayName = "get the date as expected in local time")]
  public async Task GetTheDateAsExpected()
  {
    await using var setup = await Initializer.Do();
    var entityId = Guid.NewGuid();
    var now = DateTime.Now;
    var saveEntityWithDates = new SaveEntityWithDates(entityId, now);
    await setup.Command(saveEntityWithDates);
    var readModel = await setup.ReadModel<EntityWithDatesReadModel>(entityId.ToString());
    readModel.TheDate.IsCloseTo(now);
  }

  [Fact(DisplayName = "get the date as expected in UTC")]
  public async Task GetTheDateAsExpectedUtc()
  {
    await using var setup = await Initializer.Do();
    var entityId = Guid.NewGuid();
    var now = DateTime.Now.ToUniversalTime();
    var saveEntityWithDates = new SaveEntityWithDates(entityId, now);
    await setup.Command(saveEntityWithDates);
    var readModel = await setup.ReadModel<EntityWithDatesReadModel>(entityId.ToString());
    readModel.TheDate.IsCloseTo(now);
  }
}

public static class DateExtensions
{
  public static DateTime ToMilliseconds(this DateTime originalDateTime) =>
    new DateTime(
      originalDateTime.Year,
      originalDateTime.Month,
      originalDateTime.Day,
      originalDateTime.Hour,
      originalDateTime.Minute,
      originalDateTime.Second,
      originalDateTime.Millisecond).ToUniversalTime();

  public static void IsCloseTo(this DateTime actual, DateTime? expected, int milliseconds = 5) =>
    (actual as DateTime?).IsCloseTo(expected, milliseconds);

  public static void IsCloseTo(this DateTime? actual, DateTime? expected, int milliseconds = 5) =>
    Assert.True(
      actual.HasValue
      && expected.HasValue
      && Math.Abs((actual.Value.ToUniversalTime() - expected.Value.ToUniversalTime()).TotalMilliseconds) < milliseconds,
      $"Dates are not close enough: {actual:yyyy-MM-ddTHH:mm:ss.fffffffzzz} and {expected:yyyy-MM-ddTHH:mm:ss.fffffffzzz} are more than {milliseconds} milliseconds apart.");

  public static void IsCloseTo(this DateTimeOffset actual, DateTimeOffset? expected, int milliseconds = 5) =>
    (actual as DateTimeOffset?).IsCloseTo(expected, milliseconds);

  public static void IsCloseTo(this DateTimeOffset? actual, DateTimeOffset? expected, int milliseconds = 5) =>
    Assert.True(
      actual.HasValue
      && expected.HasValue
      && Math.Abs((actual.Value.ToUniversalTime() - expected.Value.ToUniversalTime()).TotalMilliseconds) < milliseconds,
      $"Dates are not close enough: {actual:yyyy-MM-ddTHH:mm:ss.fffffffzzz} and {expected:yyyy-MM-ddTHH:mm:ss.fffffffzzz} are more than {milliseconds} milliseconds apart.");
}
