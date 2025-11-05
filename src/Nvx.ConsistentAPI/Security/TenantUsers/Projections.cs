using KurrentDB.Client;

namespace Nvx.ConsistentAPI.TenantUsers;

public class UserAddedToTenantProjection :
  ProjectionDefinition<AddedToTenant, UserWasAddedToTenant, UserSecurity, TenantUsersEntity, StrongGuid>
{
  public override string Name => "projection-user-added-to-tenant-to-tenant-users";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<UserWasAddedToTenant> Project(
    AddedToTenant eventToProject,
    UserSecurity e,
    Option<TenantUsersEntity> projectionEntity,
    StrongGuid projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    new UserWasAddedToTenant(eventToProject.TenantId, eventToProject.Sub);

  public override IEnumerable<StrongGuid> GetProjectionIds(
    AddedToTenant sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId) => [new(sourceEvent.TenantId)];
}

public class UserAddedToTenantFeedbackProjection :
  ProjectionDefinition<UserWasAddedToTenant, TenantDetailsReceived, TenantUsersEntity, UserSecurity, StrongString>
{
  public override string Name => "projection-user-added-to-tenant-feedback-to-user-security";
  public override string SourcePrefix => TenantUsersEntity.StreamPrefix;

  public override Option<TenantDetailsReceived> Project(
    UserWasAddedToTenant eventToProject,
    TenantUsersEntity e,
    Option<UserSecurity> projectionEntity,
    StrongString projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    new TenantDetailsReceived(eventToProject.UserId, e.TenantId, e.TenantName);

  public override IEnumerable<StrongString> GetProjectionIds(
    UserWasAddedToTenant sourceEvent,
    TenantUsersEntity sourceEntity,
    Uuid sourceEventId) => [new(sourceEvent.UserId)];
}

public class UserRemovedFromTenantProjection :
  ProjectionDefinition<RemovedFromTenant, UserWasRemovedFromTenant, UserSecurity, TenantUsersEntity, StrongGuid>
{
  public override string Name => "projection-user-removed-from-tenant-to-tenant-users";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<UserWasRemovedFromTenant> Project(
    RemovedFromTenant eventToProject,
    UserSecurity e,
    Option<TenantUsersEntity> projectionEntity,
    StrongGuid projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    new UserWasRemovedFromTenant(eventToProject.TenantId, eventToProject.Sub);

  public override IEnumerable<StrongGuid> GetProjectionIds(
    RemovedFromTenant sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId) => [new(sourceEvent.TenantId)];
}

public class TenantNameUpdatedProjection :
  ProjectionDefinition<TenantRenamed, TenantNameWasChanged, Tenant, TenantUsersEntity, StrongGuid>
{
  public override string Name => "projection-tenant-name-updated-to-tenant-users";
  public override string SourcePrefix => Tenant.StreamPrefix;

  public override Option<TenantNameWasChanged> Project(
    TenantRenamed eventToProject,
    Tenant e,
    Option<TenantUsersEntity> projectionEntity,
    StrongGuid projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    new TenantNameWasChanged(eventToProject.Id, eventToProject.NewName);

  public override IEnumerable<StrongGuid> GetProjectionIds(
    TenantRenamed sourceEvent,
    Tenant sourceEntity,
    Uuid sourceEventId) => [new(sourceEvent.Id)];
}

public class TenantCreatedProjection :
  ProjectionDefinition<TenantCreated, TenantNameWasChanged, Tenant, TenantUsersEntity, StrongGuid>
{
  public override string Name => "projection-tenant-created-to-tenant-users";
  public override string SourcePrefix => Tenant.StreamPrefix;

  public override Option<TenantNameWasChanged> Project(
    TenantCreated eventToProject,
    Tenant e,
    Option<TenantUsersEntity> projectionEntity,
    StrongGuid projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    new TenantNameWasChanged(eventToProject.Id, eventToProject.Name);

  public override IEnumerable<StrongGuid> GetProjectionIds(
    TenantCreated sourceEvent,
    Tenant sourceEntity,
    Uuid sourceEventId) => [new(sourceEvent.Id)];
}

public class TenantNameSetFeedbackProjection :
  ProjectionDefinition<TenantNameWasChanged, TenantDetailsReceived, TenantUsersEntity, UserSecurity, StrongString>
{
  public override string Name => "projection-tenant-name-set-feedback-to-user-security";
  public override string SourcePrefix => TenantUsersEntity.StreamPrefix;

  public override Option<TenantDetailsReceived> Project(
    TenantNameWasChanged eventToProject,
    TenantUsersEntity e,
    Option<UserSecurity> projectionEntity,
    StrongString projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) => new TenantDetailsReceived(projectionId.Value, e.TenantId, eventToProject.NewName);

  public override IEnumerable<StrongString> GetProjectionIds(
    TenantNameWasChanged sourceEvent,
    TenantUsersEntity sourceEntity,
    Uuid sourceEventId) =>
    sourceEntity.Users.Select(u => new StrongString(u)).ToArray();
}
