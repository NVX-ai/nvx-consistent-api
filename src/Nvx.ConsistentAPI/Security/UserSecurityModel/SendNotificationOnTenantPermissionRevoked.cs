using KurrentDB.Client;
using Nvx.ConsistentAPI.Framework.SignalRMessage;

namespace Nvx.ConsistentAPI;

public class SendNotificationOnTenantPermissionRevoked :
  ProjectionDefinition<TenantPermissionRevoked, SignalRMessageScheduled, UserSecurity, SignalRMessageEntity,
    SignalRMessageId>
{
  public override string Name => "projection-tenant-permission-revoked-to-send-notification";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<SignalRMessageScheduled> Project(
    TenantPermissionRevoked eventToProject,
    UserSecurity e,
    Option<SignalRMessageEntity> projectionEntity,
    SignalRMessageId projectionId,
    Uuid sourceEventUuid,
    EventMetadata metadata) =>
    new SignalRMessageScheduled(
      projectionId.Value,
      [eventToProject.Sub],
      $"Tenant Permission {eventToProject.Permission} revoked",
      "tenant-permission-revoked",
      e.Sub,
      e.GetType().Name,
      e.Sub,
      null,
      "permission",
      DateTime.UtcNow);

  public override IEnumerable<SignalRMessageId> GetProjectionIds(
    TenantPermissionRevoked sourceEvent,
    UserSecurity sourceEntity,
    Uuid sourceEventId) => [new(Guid.NewGuid())];
}
