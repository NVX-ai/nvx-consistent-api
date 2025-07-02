namespace Nvx.ConsistentAPI.Tests.Security;

public class TemplateUserRoleIntegration
{
  [Fact(DisplayName = "Template is created and updated, with roles added and removed")]
  public async Task TemplateUserRole()
  {
    await using var setup = await Initializer.Do();
    var templateName = $"Template named: {Guid.NewGuid()}";
    var templateDescription = $"Template description: {Guid.NewGuid()}";
    var templateId = await setup
      .Command(new DescribeTemplateUserRole(null, templateName, templateDescription), true)
      .Map(car => car.EntityId)
      .Map(Guid.Parse);

    var permission = Guid.NewGuid().ToString();
    await setup.Command(new AddTemplateUserRolePermission(templateId, permission), true);

    await EventuallyConsistent.WaitFor(async () =>
    {
      var template = await setup.ReadModel<TemplateUserRoleReadModel>(templateId.ToString(), asAdmin: true);
      Assert.Equal(templateName, template.Name);
      Assert.Equal(templateDescription, template.Description);
      Assert.Contains(template.Permissions, p => p == permission);
    });

    var permission2 = Guid.NewGuid().ToString();
    await setup.Command(new AddTemplateUserRolePermission(templateId, permission2), true);

    await EventuallyConsistent.WaitFor(async () =>
    {
      var template = await setup.ReadModel<TemplateUserRoleReadModel>(templateId.ToString(), asAdmin: true);
      Assert.Contains(template.Permissions, p => p == permission);
      Assert.Contains(template.Permissions, p => p == permission2);
      Assert.Equal(2, template.Permissions.Length);
    });

    await setup.Command(new RemoveTemplateUserRolePermission(templateId, permission), true);

    await EventuallyConsistent.WaitFor(async () =>
    {
      var template = await setup.ReadModel<TemplateUserRoleReadModel>(templateId.ToString(), asAdmin: true);
      Assert.DoesNotContain(template.Permissions, p => p == permission);
      Assert.Contains(template.Permissions, p => p == permission2);
      Assert.Single(template.Permissions);
    });

    var newTemplateName = $"Updated template name: {Guid.NewGuid()}";
    var newTemplateDescription = $"Updated template description: {Guid.NewGuid()}";
    await setup.Command(new DescribeTemplateUserRole(templateId, newTemplateName, newTemplateDescription), true);

    await EventuallyConsistent.WaitFor(async () =>
    {
      var template = await setup.ReadModel<TemplateUserRoleReadModel>(templateId.ToString(), asAdmin: true);
      Assert.Equal(newTemplateName, template.Name);
      Assert.Equal(newTemplateDescription, template.Description);
      Assert.DoesNotContain(template.Permissions, p => p == permission);
      Assert.Contains(template.Permissions, p => p == permission2);
      Assert.Single(template.Permissions);
    });
  }

  [Fact(DisplayName = "Role is created from template")]
  public async Task RoleFromTemplate()
  {
    await using var setup = await Initializer.Do();
    var templateName = $"Template named: {Guid.NewGuid()}";
    var templateDescription = $"Template description: {Guid.NewGuid()}";
    var tenantId = Guid.NewGuid();
    var permissionName = $"test-permission-{Guid.NewGuid()}";
    await setup.Command(new AddToTenant(setup.Auth.CandoSub), true, tenantId);
    var templateId = await setup
      .Command(new DescribeTemplateUserRole(null, templateName, templateDescription), true)
      .Map(car => car.EntityId)
      .Map(Guid.Parse);
    await setup.Command(new AddTemplateUserRolePermission(templateId, permissionName), true);
    await setup.Command(new CreateRoleFromTemplate(templateId), true, tenantId);

    await EventuallyConsistent.WaitFor(async () =>
    {
      var roles = await setup.ReadModels<RoleReadModel>(tenantId: tenantId);
      Assert.Single(roles.Items);
      Assert.Single(roles.Items.Single().Permissions);
      Assert.Equal(templateName, roles.Items.Single().Name);
      Assert.Equal(templateDescription, roles.Items.Single().Description);
      Assert.Equal(permissionName, roles.Items.Single().Permissions.Single());
    });
  }
}
