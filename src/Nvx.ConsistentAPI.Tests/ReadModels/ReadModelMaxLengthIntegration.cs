using System.Security.Cryptography;
using System.Text;

namespace Nvx.ConsistentAPI.Tests.ReadModels;

public class ReadModelMaxLengthIntegration
{
  [Fact(DisplayName = "ReadModel with max length fields should not throw exceptions")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();

    var name = Guid.NewGuid() + GenerateRandomString(300);
    var tenantId = Guid.NewGuid();
    await setup
      .Command(new RegisterOrganizationBuilding(name), tenantId: tenantId, asAdmin: true)
      .Map(m => m.EntityId);
    var model = await setup.ReadModels<OrganizationBuildingReadModel>(
    queryParameters: new Dictionary<string, string[]> {{"name", [name]}},
    tenantId: tenantId, asAdmin: true);
    Assert.NotNull(model);
  }
  
  private static string GenerateRandomString(int length)
  {
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    var result = new StringBuilder(length);
    using var rng = RandomNumberGenerator.Create();
    var buffer = new byte[sizeof(uint)];

    for (int i = 0; i < length; i++)
    {
      rng.GetBytes(buffer);
      uint num = BitConverter.ToUInt32(buffer, 0);
      result.Append(chars[(int)(num % (uint)chars.Length)]);
    }
    return result.ToString();
  }
}
