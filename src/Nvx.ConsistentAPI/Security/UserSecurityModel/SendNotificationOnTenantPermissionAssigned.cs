using KurrentDB.Client;
using Nvx.ConsistentAPI.Framework.SignalRMessage;

namespace Nvx.ConsistentAPI;

public class SendNotificationOnTenantPermissionAssigned :
  ProjectionDefinition<TenantPermissionAssigned, SignalRMessageScheduled, UserSecurity, SignalRMessageEntity,
    SignalRMessageId>
{
  public override string Name => "projection-tenant-permission-assigned-to-send-notification";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<SignalRMessageScheduled> Project(
    TenantPermissionAssigned eventToProject,
    UserSecurity e,
    Option<SignalRMessageEntity> projectionEntity,
    SignalRMessageId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    new SignalRMessageScheduled(
      projectionId.Value,
      [eventToProject.Sub],
      $"Tenant permission {eventToProject.Permission} assigned",
      "tenant-permission-assigned",
      e.Sub,
      e.GetType().Name,
      e.Sub,
      null,
      "permission",
      DateTime.UtcNow);

  public override IEnumerable<SignalRMessageId> GetProjectionIds(
    TenantPermissionAssigned sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId) => [new(Guid.NewGuid())];
}
