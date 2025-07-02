namespace Nvx.ConsistentAPI.Framework.SignalRMessage;

public static class SignalRMessageSubModel
{
  public static EventModel Get(SendNotificationToHub sendNotificationToHub) => new()
  {
    Entities =
    [
      new EntityDefinition<SignalRMessageEntity, SignalRMessageId>
      {
        Defaulter = SignalRMessageEntity.Defaulted, StreamPrefix = SignalRMessageEntity.StreamPrefix
      }
    ],
    Tasks =
    [
      new TodoTaskDefinition<SignalRMessageData, SignalRMessageEntity, SignalRMessageScheduled, SignalRMessageId>
      {
        Type = "framework-send-signalr-message-to-hub",
        Action = (data, _, _, _, _) => SignalRMessageData.Execute(data, sendNotificationToHub),
        Originator = (_, ett, _) => new SignalRMessageData(ett.Notifications, ett.ChannelName, ett.ScheduledAt),
        SourcePrefix = SignalRMessageEntity.StreamPrefix
      }
    ]
  };
}
