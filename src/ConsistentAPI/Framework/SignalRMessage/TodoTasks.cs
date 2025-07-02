namespace ConsistentAPI.Framework.SignalRMessage;

public record SignalRMessageData(SignalRNotification[] Notifications, string ChannelName, DateTime ScheduledAt)
  : OverriddenScheduleTodo
{
  public static async Task<Du<EventInsertion, TodoOutcome>> Execute(
    SignalRMessageData data,
    SendNotificationToHub sendNotificationToHub)
  {
    foreach (var notification in data.Notifications)
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
    }

    return TodoOutcome.Done;
  }
}
