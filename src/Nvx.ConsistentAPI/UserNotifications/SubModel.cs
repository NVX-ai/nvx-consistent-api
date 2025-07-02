namespace Nvx.ConsistentAPI;

public static class UserNotificationSubModel
{
  public static readonly EventModel Get =
    new()
    {
      Entities = [UserNotificationEntity.Definition],
      Tasks =
      [
        new TodoTaskDefinition<SendNotification, UserNotificationEntity, NotificationSent, StrongString>
        {
          Action = (_, entity, _, _, _) => SendNotification.Execute(entity),
          Originator = Originator,
          Type = "send-notification-to-hub",
          SourcePrefix = UserNotificationEntity.StreamPrefix
        },
        new TodoTaskDefinition<FollowUpOnDeletion, UserNotificationEntity, NotificationDeleted, StrongString>
        {
          Action = (_, entity, _, _, _) => FollowUpOnDeletion.Execute(entity),
          Originator = (_, _, _) => new FollowUpOnDeletion(),
          Type = "follow-up-on-deletion",
          SourcePrefix = UserNotificationEntity.StreamPrefix,
          Delay = TimeSpan.FromDays(30).Add(TimeSpan.FromMinutes(1)),
          Expiration = TimeSpan.FromDays(90)
        }
      ],
      Commands =
      [
        new CommandDefinition<NotificationDelete, UserNotificationEntity>
        {
          Description = "Deletes a notification",
          Auth = new EveryoneAuthenticated(),
          AreaTag = OperationTags.Notifications
        },
        new CommandDefinition<NotificationRestore, UserNotificationEntity>
        {
          Description = "Restore a deleted notification",
          Auth = new EveryoneAuthenticated(),
          AreaTag = OperationTags.Notifications
        },
        new CommandDefinition<NotificationArchive, UserNotificationEntity>
        {
          Description = "Archive a notification",
          Auth = new EveryoneAuthenticated(),
          AreaTag = OperationTags.Notifications
        },
        new CommandDefinition<NotificationMarkAsRead, UserNotificationEntity>
        {
          Description = "Marks a notification as read",
          Auth = new EveryoneAuthenticated(),
          AreaTag = OperationTags.Notifications
        },
        new CommandDefinition<NotificationMarkAsUnread, UserNotificationEntity>
        {
          Description = "Marks a notification as unread",
          Auth = new EveryoneAuthenticated(),
          AreaTag = OperationTags.Notifications
        },
        new CommandDefinition<NotificationMarkAsFavorite, UserNotificationEntity>
        {
          Description = "Marks a notification as favorite",
          Auth = new EveryoneAuthenticated(),
          AreaTag = OperationTags.Notifications
        },
        new CommandDefinition<NotificationMarkAsUnfavorite, UserNotificationEntity>
        {
          Description = "Unchecks a notification as favorite",
          Auth = new EveryoneAuthenticated(),
          AreaTag = OperationTags.Notifications
        }
      ],
      ReadModels =
      [
        new ReadModelDefinition<UserNotificationReadModel, UserNotificationEntity>
        {
          StreamPrefix = UserNotificationEntity.StreamPrefix,
          Projector = UserNotificationReadModel.FromEntity,
          AreaTag = OperationTags.Notifications
        },
        new ReadModelDefinition<UserNotificationDeletedReadModel, UserNotificationEntity>
        {
          StreamPrefix = UserNotificationEntity.StreamPrefix,
          Projector = UserNotificationDeletedReadModel.FromEntity,
          AreaTag = OperationTags.Notifications
        }
      ]
    };

  private static SendNotification Originator(
    NotificationSent notificationSent,
    UserNotificationEntity entity,
    EventMetadata eventMetadata) =>
    new(entity.Id);
}
