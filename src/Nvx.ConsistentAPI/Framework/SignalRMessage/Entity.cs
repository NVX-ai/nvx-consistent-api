namespace Nvx.ConsistentAPI.Framework.SignalRMessage;

public record SignalRMessageId(Guid Value) : StrongId
{
  public override string StreamId() => Value.ToString();
  public override string ToString() => StreamId();
}

public partial record SignalRMessageEntity(
  Guid Id,
  SignalRNotification[] Notifications,
  string ChannelName,
  DateTime ScheduledAt)
  : EventModelEntity<SignalRMessageEntity>,
    Folds<SignalRMessageScheduled, SignalRMessageEntity>
{
  public const string StreamPrefix = "framework-signalr-message-";
  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<SignalRMessageEntity> Fold(
    SignalRMessageScheduled evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult<SignalRMessageEntity>(
      new SignalRMessageEntity(
        Id,
        evt
          .UserSubs.Select(sub => new SignalRNotification(
            sub,
            evt.Message,
            evt.MessageType,
            evt.RelatedEntityId,
            evt.RelatedEntityType,
            evt.SenderSub,
            evt.NotificationId))
          .ToArray(),
        evt.ChannelName,
        evt.ScheduledAt));

  public static string GetStreamName(Guid id) => $"{StreamPrefix}{id}";

  public static SignalRMessageEntity Defaulted(SignalRMessageId id) =>
    new(id.Value, [], "", new DateTime(2000, 1, 1));
}
