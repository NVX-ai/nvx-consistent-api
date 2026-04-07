using Microsoft.Extensions.Logging;

namespace Nvx.ConsistentAPI.Framework.SignalRMessage;

public record SignalRMessageData(SignalRNotification[] Notifications, string ChannelName, DateTime ScheduledAt)
  : OverriddenScheduleTodo
{
  public static async Task<Du<EventInsertion, TodoOutcome>> Execute(
    SignalRMessageData data,
    SendNotificationToHub sendNotificationToHub,
    ILogger logger)
  {
    logger.LogDebug(
      "SignalRMessageData.Execute: sending {NotificationCount} notifications on channel {ChannelName} (scheduled at {ScheduledAt})",
      data.Notifications.Length,
      data.ChannelName,
      data.ScheduledAt);

    var successCount = 0;
    var failureCount = 0;

    foreach (var notification in data.Notifications)
    {
      logger.LogDebug(
        "Sending SignalR notification to user {UserSub}, type {MessageType}, notificationId {NotificationId}, relatedEntityType {RelatedEntityType}, relatedEntityId {RelatedEntityId}, channel {ChannelName}",
        notification.UserSub,
        notification.MessageType,
        notification.NotificationId,
        notification.RelatedEntityType,
        notification.RelatedEntityId,
        data.ChannelName);

      try
      {
        await sendNotificationToHub(
          notification.UserSub,
          notification.Message,
          notification.MessageType,
          notification.RelatedEntityId,
          notification.RelatedEntityType,
          notification.SenderSub,
          notification.NotificationId,
          data.ChannelName);
        successCount++;
      }
      catch (Exception ex)
      {
        failureCount++;
        logger.LogError(
          ex,
          "SignalR send failed for user {UserSub}, type {MessageType}, notificationId {NotificationId}, relatedEntityType {RelatedEntityType}, relatedEntityId {RelatedEntityId}, channel {ChannelName} — continuing with remaining notifications",
          notification.UserSub,
          notification.MessageType,
          notification.NotificationId,
          notification.RelatedEntityType,
          notification.RelatedEntityId,
          data.ChannelName);
      }
    }

    logger.LogDebug(
      "SignalRMessageData.Execute: completed — {SuccessCount} sent, {FailureCount} failed of {TotalCount} total",
      successCount,
      failureCount,
      data.Notifications.Length);

    return TodoOutcome.Done;
  }
}
