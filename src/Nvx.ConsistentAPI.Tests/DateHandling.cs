namespace Nvx.ConsistentAPI.Tests;

public class DateHandling
{
  public static TheoryData<int> OffsetData => new(Enumerable.Range(-14, 29).OrderBy(_ => Random.Shared.Next()).Take(5));

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

  [Theory(DisplayName = "filters on date only")]
  [MemberData(nameof(OffsetData))]
  public async Task Test(int offset)
  {
    await using var setup = await Initializer.Do();
    var entityId = Guid.NewGuid();
    var today = DateTime.UtcNow;
    var theDate = new DateTimeOffset(today.Year, today.Month, today.Day, 0, 0, 0, TimeSpan.FromHours(offset));
    await setup.Command(new SaveEntityWithDates(entityId, theDate));
    Assert.Single(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "eq-OnlyTheDate",
              [theDate.ToLocalDateOnly().ToString("yyyy/MM/dd")]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Empty(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "eq-OnlyTheDate",
              [theDate.ToLocalDateOnly().AddDays(120).ToString("yyyy-MM-dd")]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Empty(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "gt-OnlyTheDate",
              [theDate.ToLocalDateOnly().AddDays(1).ToString("yyyy-MM-dd")]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Empty(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "gte-OnlyTheDate",
              [theDate.ToLocalDateOnly().AddDays(1).ToString("yyyy-MM-dd")]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Empty(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "lt-OnlyTheDate",
              [theDate.ToLocalDateOnly().AddDays(-1).ToString("yyyy-MM-dd")]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Empty(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "lte-OnlyTheDate",
              [theDate.ToLocalDateOnly().AddDays(-1).ToString("yyyy-MM-dd")]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Single(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "gte-TheDate",
              [theDate.ToString()]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Single(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "gt-TheDate",
              [theDate.AddSeconds(-1).ToString()]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Single(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "lte-TheDate",
              [theDate.ToString()]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Single(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "lt-TheDate",
              [theDate.AddSeconds(1).ToString()]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Empty(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "gte-TheDate",
              [theDate.AddSeconds(1).ToString()]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Empty(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "gt-TheDate",
              [theDate.ToString()]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Empty(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "lte-TheDate",
              [theDate.AddSeconds(-1).ToString()]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));

    Assert.Empty(
      await setup
        .ReadModels<EntityWithDatesReadModel>(
          queryParameters: new Dictionary<string, string[]>
          {
            {
              "lt-TheDate",
              [theDate.ToString()]
            },
            { "eq-Id", [entityId.ToString()] }
          })
        .Map(r => r.Items));
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
