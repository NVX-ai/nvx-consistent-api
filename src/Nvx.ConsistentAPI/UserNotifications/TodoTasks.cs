using Nvx.ConsistentAPI.Framework.SignalRMessage;

namespace Nvx.ConsistentAPI;

public delegate Task SendNotificationToHub(
  string userSub,
  string message,
  string messageType,
  string? relatedEntityId,
  string? relatedEntityType,
  string? senderSub,
  string? notificationId,
  string? channelName);

public record SendNotification(string Id) : TodoData
{
  public static Task<Du<EventInsertion, TodoOutcome>> Execute(UserNotificationEntity ett) =>
    Task.FromResult<Du<EventInsertion, TodoOutcome>>(
      new AnyState(
        new SignalRMessageScheduled(
          Guid.NewGuid(),
          [ett.UserSub],
          ett.Message,
          ett.MessageType,
          ett.RelatedEntityId,
          ett.RelatedEntityType,
          ett.SenderSub,
          ett.Id,
          "notification",
          DateTime.UtcNow)));
}

public record FollowUpOnDeletion : TodoData
{
  public static Task<Du<EventInsertion, TodoOutcome>> Execute(UserNotificationEntity un) =>
    un.State == UserNotificationState.SoftDeleted
    && un.DeletedAt <= DateTime.UtcNow.AddDays(-30)
      ? Task.FromResult(
        Du<EventInsertion, TodoOutcome>.First(
          new AnyState(new NotificationPermanentlyDeleted(un.Id))))
      : Task.FromResult(Du<EventInsertion, TodoOutcome>.Second(TodoOutcome.Done));
}
