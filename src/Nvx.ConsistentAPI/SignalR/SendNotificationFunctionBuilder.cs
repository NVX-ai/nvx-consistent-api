using Microsoft.AspNetCore.SignalR;

namespace Nvx.ConsistentAPI.SignalR;

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
