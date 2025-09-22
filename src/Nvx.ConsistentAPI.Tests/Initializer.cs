namespace Nvx.ConsistentAPI.Tests;

public static class Initializer
{
  public static async Task<TestSetup> Do() => await TestSetup.Initialize(
    TestModel.GetModel(),
    new TestSettings
    {
      LogsFolder = "logs",
      UsePersistentTestContainers = false,
      HydrationParallelism = 10
    });
}
