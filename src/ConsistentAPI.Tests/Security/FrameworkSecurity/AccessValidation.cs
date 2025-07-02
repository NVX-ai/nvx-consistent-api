namespace ConsistentAPI.Tests.Security;

public class AccessValidation
{
  [Fact(DisplayName = "User with single required tenant Permission is validated")]
  public async Task UserWithTenantPermissionIsValidated()
  {
    var auth = new PermissionsRequireOne("Permission");
    var tenantId = Guid.NewGuid();
    var user = new UserSecurity(
      "00000000-0000-0000-0000-000000000000",
      "mail@mail.com",
      "user",
      new Dictionary<Guid, UserSecurity.ReceivedRole[]>(),
      [],
      new Dictionary<Guid, string[]> { [tenantId] = ["Permission"] },
      [new TenantDetails(tenantId, $"Tenant {tenantId}")]
    );
    await FrameworkSecurity.Validate(auth, user, tenantId).ShouldBeOk();
  }
}
