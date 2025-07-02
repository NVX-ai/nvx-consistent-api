namespace Nvx.ConsistentAPI;

public static class TemplateUserRoleSubModel
{
  public static readonly EventModel Get = new()
  {
    Entities =
    [
      new EntityDefinition<TemplateUserRoleEntity, TemplateUserRoleId>
      {
        Defaulter = TemplateUserRoleEntity.Defaulted,
        StreamPrefix = TemplateUserRoleEntity.StreamPrefix
      }
    ],
    Commands =
    [
      new CommandDefinition<DescribeTemplateUserRole, TemplateUserRoleEntity>
      {
        AreaTag = OperationTags.Authorization,
        Auth = new PermissionsRequireOne("admin")
      },
      new CommandDefinition<RemoveTemplateUserRole, TemplateUserRoleEntity>
      {
        AreaTag = OperationTags.Authorization,
        Auth = new PermissionsRequireOne("admin")
      },
      new CommandDefinition<AddTemplateUserRolePermission, TemplateUserRoleEntity>
      {
        AreaTag = OperationTags.Authorization,
        Auth = new PermissionsRequireOne("admin")
      },
      new CommandDefinition<RemoveTemplateUserRolePermission, TemplateUserRoleEntity>
      {
        AreaTag = OperationTags.Authorization,
        Auth = new PermissionsRequireOne("admin")
      }
    ],
    ReadModels =
    [
      new ReadModelDefinition<TemplateUserRoleReadModel, TemplateUserRoleEntity>
      {
        StreamPrefix = TemplateUserRoleEntity.StreamPrefix,
        Projector = TemplateUserRoleReadModel.From,
        AreaTag = OperationTags.Authorization
      }
    ]
  };
}
