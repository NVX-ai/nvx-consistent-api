namespace Nvx.ConsistentAPI.SignalR;

public record SignalRNotification(
  string UserSub,
  string Message,
  string MessageType,
  string? RelatedEntityId,
  string? RelatedEntityType,
  string? SenderSub,
  string? NotificationId);
