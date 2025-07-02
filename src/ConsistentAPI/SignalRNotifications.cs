using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ConsistentAPI;

public record SignalRNotification(
  string UserSub,
  string Message,
  string MessageType,
  string? RelatedEntityId,
  string? RelatedEntityType,
  string? SenderSub,
  string? NotificationId);

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class NotificationHub : Hub;

public static class SendNotificationFunctionBuilder
{
  public static SendNotificationToHub Build(IHubContext<NotificationHub> hubContext) =>
    async (
      userSub,
      message,
      messageType,
      relatedEntityId,
      relatedEntityType,
      senderSub,
      notificationId,
      channelName) => await hubContext
      .Clients
      .User(userSub)
      .SendAsync(
        channelName ?? "notification",
        new SignalRNotification(
          userSub,
          message,
          messageType,
          relatedEntityId,
          relatedEntityType,
          senderSub,
          notificationId));
}
