namespace Nvx.ConsistentAPI.Tests.Framework.Idempotency;

public class IdempotencyTests
{
  [Fact(DisplayName = "handles idempotent commands")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var tenant1Id = Guid.NewGuid();
    var idempotencyKey = Guid.NewGuid().ToString();

    Assert.Equal(
      new CommandAcceptedResult(tenant1Id.ToString()),
      await setup.Command(
        new CreateTenant(tenant1Id, "some idempotent tenant"),
        true,
        headers: new Dictionary<string, string> { ["IdempotencyKey"] = idempotencyKey }));
    Assert.Equal(
      new CommandAcceptedResult(tenant1Id.ToString()),
      await setup.Command(
        new CreateTenant(tenant1Id, "some idempotent tenant"),
        true,
        headers: new Dictionary<string, string> { ["IdempotencyKey"] = idempotencyKey }));
    Assert.Equal(
      new CommandAcceptedResult(tenant1Id.ToString()),
      await setup.Command(
        new CreateTenant(tenant1Id, "some idempotent tenant"),
        true,
        headers: new Dictionary<string, string> { ["IdempotencyKey"] = idempotencyKey }));
  }
}
