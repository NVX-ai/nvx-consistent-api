using Microsoft.AspNetCore.SignalR;
using Nvx.ConsistentAPI.Framework.Projections.Model;
using Nvx.ConsistentAPI.Framework.SignalRMessage;
using Nvx.ConsistentAPI.TenantUsers;

namespace Nvx.ConsistentAPI;

public static class FrameworkEventModel
{
  public static EventModel Model(
    GeneratorSettings settings,
    IHubContext<NotificationHub> hubContext) =>
    UserSecurity
      .Get
      .Merge(UserWithPermissionProjection.Get)
      .Merge(UserWithTenantPermissionProjection.Get)
      .Merge(Tenant.Get)
      .Merge(FileUpload.Get(settings))
      .Merge(ProcessorEntity.Get)
      .Merge(RecurringTaskExecution.Get)
      .Merge(FrameworkValidationRuleEntity.Get)
      .Merge(IdempotencyCache.Get)
      .Merge(UserNotificationSubModel.Get)
      .Merge(TenantUsersSubModel.Get)
      .Merge(SignalRMessageSubModel.Get(SendNotificationFunctionBuilder.Build(hubContext)))
      .Merge(ProjectionTrackingModel.Get)
      .Merge(RolesModel.Get)
      .Merge(TemplateUserRoleSubModel.Get)
      .Merge(DynamicConsistencyBoundaryModel.Get);
}
