namespace Nvx.ConsistentAPI.Framework.SignalRMessage;

public record SignalRMessageScheduled(
  Guid Id,
  string[] UserSubs,
  string Message,
  string MessageType,
  string? RelatedEntityId,
  string? RelatedEntityType,
  string? SenderSub,
  string? NotificationId,
  string ChannelName,
  DateTime ScheduledAt)
  : EventModelEvent
{
  public string GetStreamName() => SignalRMessageEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new SignalRMessageId(Id);
}
