namespace Nvx.ConsistentAPI;

public static class RolesModel
{
  public static readonly EventModel Get = new()
  {
    Entities =
    [
      new EntityDefinition<RoleEntity, RoleId>
      {
        Defaulter = RoleEntity.Defaulted,
        StreamPrefix = RoleEntity.StreamPrefix
      }
    ],
    ReadModels =
    [
      new ReadModelDefinition<RoleReadModel, RoleEntity>
      {
        StreamPrefix = RoleEntity.StreamPrefix,
        Projector = role =>
        [
          new RoleReadModel(
            new RoleId(role.Id, role.TenantId).ToString(),
            role.Id,
            role.Name,
            role.Description,
            role.Permissions,
            role.TenantId)
        ],
        AreaTag = OperationTags.Authorization
      }
    ],
    Commands =
    [
      new CommandDefinition<AddPermissionToRole, RoleEntity>
      {
        Auth = new PermissionsRequireOne("admin"),
        AreaTag = OperationTags.Authorization
      },
      new CommandDefinition<RemovePermissionFromRole, RoleEntity>
      {
        Auth = new PermissionsRequireOne("admin"),
        AreaTag = OperationTags.Authorization
      },
      new CommandDefinition<CreateRole, RoleEntity>
      {
        Auth = new PermissionsRequireOne("admin"),
        AreaTag = OperationTags.Authorization
      },
      new CommandDefinition<CreateRoleFromTemplate, RoleEntity>
      {
        Auth = new PermissionsRequireOne("admin"),
        AreaTag = OperationTags.Authorization
      }
    ]
  };
}
