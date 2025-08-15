namespace Nvx.ConsistentAPI.Tests.UserNotifications.ReadModels;

public class UserNotificationDeletedReadModelShould
{
  [Fact(DisplayName = "Project as expected")]
  public async Task ProjectAsExpected()
  {
    var recipient = TestData.UserWithNoPermissions();
    var sender = TestData.UserWithNoPermissions();
    var notificationId = Guid.NewGuid().ToString();
    var message = Guid.NewGuid().ToString();
    var messageType = Guid.NewGuid().ToString();
    var relatedEntityId = Guid.NewGuid().ToString();
    var relatedEntityType = Guid.NewGuid().ToString();

    var entity = await UserNotificationEntity
      .Defaulted(new StrongString(notificationId))
      .Fold(
        new NotificationSent(
          notificationId,
          recipient.Sub,
          message,
          messageType,
          relatedEntityId,
          relatedEntityType,
          sender.Sub,
          DateTime.UtcNow,
          null),
        new EventMetadata(DateTime.UtcNow, null, null, null, null, null),
        null!);

    Assert.Empty(
      UserNotificationDeletedReadModel
        .FromEntity(
          await entity.Fold(
            new NotificationRestored(notificationId),
            new EventMetadata(DateTime.UtcNow, null, null, null, null, null),
            null!)));
  }
}
