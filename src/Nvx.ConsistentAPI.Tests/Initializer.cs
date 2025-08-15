namespace Nvx.ConsistentAPI.Tests;

public static class Initializer
{
  public static async Task<TestSetup> Do() => await TestSetup.Initialize(
    TestModel.GetModel(),
    new TestSettings
    {
      LogsFolder = "logs",
      UsePersistentTestContainers =
        Environment
          .GetEnvironmentVariable("USE_PERISTENT_TEST_CONTAINERS")
          ?.Equals("true", StringComparison.InvariantCultureIgnoreCase)
        == true,
      StoreType =
        Environment
            .GetEnvironmentVariable("TEST_EVENT_STORE_TYPE")
            ?.ToLowerInvariant() switch
          {
            "inmemory" => EventStoreType.InMemory,
            _ => EventStoreType.EventStoreDb
          }
    });
}
