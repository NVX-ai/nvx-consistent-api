namespace ConsistentAPI.Tests;

public static class TestData
{
  public static UserSecurity UserWithNoPermissions() =>
    new(
      Guid.NewGuid().ToString(),
      $"{Guid.NewGuid().ToString()}@email.com",
      $"TestUser {Guid.NewGuid().ToString()}",
      new Dictionary<Guid, UserSecurity.ReceivedRole[]>(),
      [],
      new Dictionary<Guid, string[]>(),
      []
    );
}
