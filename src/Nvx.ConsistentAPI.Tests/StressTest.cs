namespace Nvx.ConsistentAPI.Tests;

public class StressTest
{
  [Fact(DisplayName = "The application can take a lot of commands in a short time")]
  public async Task Test1()
  {
    await using var setup = await Initializer.Do();
    var countId = Guid.NewGuid();
    const int count = 100;
    await Enumerable
      .Range(0, count)
      .Select<int, Func<Task<Unit>>>(_ => async () =>
      {
        // ReSharper disable once AccessToDisposedClosure
        await setup.Command(new MakeItCount(countId));
        return unit;
      })
      .Parallel();
    var readModel = await setup.ReadModel<ExtremeCountReadModel>(countId.ToString());
    Assert.Equal(count, readModel.Count);
  }

  [Fact(DisplayName = "The application can take a lot of commands in different streams a short time")]
  public async Task Test2()
  {
    await using var setup = await Initializer.Do();
    var ids = Enumerable
      .Range(0, 20)
      .Select(_ => Guid.NewGuid())
      .ToArray();
    await ids
      .Select<Guid, Func<Task<Unit>>>(id => async () =>
      {
        // ReSharper disable once AccessToDisposedClosure
        await setup.Command(new MakeItCount(id));
        // ReSharper disable once AccessToDisposedClosure
        await setup.Command(new MakeItCount(id));
        // ReSharper disable once AccessToDisposedClosure
        await setup.Command(new MakeItCount(id));
        // ReSharper disable once AccessToDisposedClosure
        await setup.Command(new MakeItCount(id));
        return unit;
      })
      .Parallel();
    foreach (var id in ids)
    {
      var readModel = await setup.ReadModel<ExtremeCountReadModel>(id.ToString());
      Assert.Equal(4, readModel.Count);
    }
  }
}
