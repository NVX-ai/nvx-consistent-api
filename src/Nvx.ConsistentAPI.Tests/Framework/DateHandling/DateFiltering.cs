namespace Nvx.ConsistentAPI.Tests;

public class DateFiltering
{
  public static TheoryData<int> OffsetData => new(Enumerable.Range(-14, 29).OrderBy(_ => Random.Shared.Next()).Take(2));

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
