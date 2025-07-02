namespace ConsistentAPI.TenantUsers;

public static class TenantUsersSubModel
{
  public static readonly EventModel Get =
    new()
    {
      Entities =
      [
        new EntityDefinition<TenantUsersEntity, StrongGuid>
        {
          Defaulter = TenantUsersEntity.Defaulted, StreamPrefix = TenantUsersEntity.StreamPrefix
        }
      ],
      ReadModels =
      [
        new ReadModelDefinition<TenantUserReadModel, TenantUsersEntity>
        {
          StreamPrefix = "tenant-users-entity-",
          Projector = entity => [new TenantUserReadModel(entity.TenantId.ToString(), entity.TenantName, entity.Users)],
          Auth = new PermissionsRequireOne("tenancy-management", "tenancy-read"),
          AreaTag = OperationTags.TenancyManagement
        }
      ],
      Projections =
      [
        new UserAddedToTenantProjection(),
        new UserRemovedFromTenantProjection(),
        new TenantNameUpdatedProjection(),
        new TenantCreatedProjection(),
        new UserAddedToTenantFeedbackProjection(),
        new TenantNameSetFeedbackProjection()
      ]
    };
}
