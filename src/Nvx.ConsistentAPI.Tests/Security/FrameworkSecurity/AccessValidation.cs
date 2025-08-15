namespace Nvx.ConsistentAPI.Tests.Security;

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

  [Fact(DisplayName = "handles access to commands and read models")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    await setup.UnauthorizedCommand(new CreateProduct(Guid.NewGuid(), "banana", null));
    await setup.ForbiddenCommand(new CreateProduct(Guid.NewGuid(), "banana", null));
    await setup.ForbiddenReadModel<UserWithPermissionReadModel>();

    await setup.FailingCommand(new CommandThatLikesAdmins(), 409, asAdmin: true);
    await setup.ForbiddenCommand(new CommandThatLikesAdmins());
  }
}
