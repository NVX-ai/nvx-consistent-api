namespace ConsistentAPI;

public record TemplateUserRoleReadModel(
  string Id,
  Guid TemplateUserRoleId,
  string Name,
  string Description,
  string[] Permissions
) : EventModelReadModel
{
  public StrongId GetStrongId() => new TemplateUserRoleId(TemplateUserRoleId);

  public static TemplateUserRoleReadModel[] From(TemplateUserRoleEntity entity) =>
    entity.IsDeleted
      ? []
      :
      [
        new TemplateUserRoleReadModel(
          entity.TemplateUserRoleId.ToString(),
          entity.TemplateUserRoleId,
          entity.Name,
          entity.Description,
          entity.Permissions)
      ];
}
