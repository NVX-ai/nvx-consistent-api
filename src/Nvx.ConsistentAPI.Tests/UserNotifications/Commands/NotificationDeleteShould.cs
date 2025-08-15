namespace Nvx.ConsistentAPI.Tests.UserNotifications.Commands;

public class NotificationDeleteShould
{
  [Fact(DisplayName = "Delete notification")]
  public async Task DeleteNotification()
  {
    var recipient = TestData.UserWithNoPermissions();
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
          null,
          DateTime.UtcNow,
          null),
        new EventMetadata(DateTime.UtcNow, null, null, null, null, null),
        null!);
    Assert.False(entity.IsRead);
    var decision = new NotificationDelete(notificationId).Decide(entity, recipient, []);
    decision.ShouldBeOk(ei =>
    {
      Assert.True(ei is AnyState);
      Assert.Single(((AnyState)ei).Events);
      Assert.Equal(new NotificationDeleted(notificationId), ((AnyState)ei).Events[0]);
      return unit;
    });
  }

  [Fact(DisplayName = "Forbid other users")]
  public async Task ForbidsOthers()
  {
    var recipient = TestData.UserWithNoPermissions();
    var notificationId = Guid.NewGuid().ToString();
    var userSub = Guid.NewGuid().ToString();
    var message = Guid.NewGuid().ToString();
    var messageType = Guid.NewGuid().ToString();
    var relatedEntityId = Guid.NewGuid().ToString();
    var relatedEntityType = Guid.NewGuid().ToString();

    var entity = UserNotificationEntity
      .Defaulted(new StrongString(notificationId))
      .Fold(
        new NotificationSent(
          notificationId,
          userSub,
          message,
          messageType,
          relatedEntityId,
          relatedEntityType,
          null,
          DateTime.UtcNow,
          null),
        new EventMetadata(DateTime.UtcNow, null, null, null, null, null),
        null!);
    var command = new NotificationDelete(notificationId);
    Assert.Equal(new StrongString(notificationId), command.TryGetEntityId(recipient));
    var decision = command.Decide(await entity, recipient, []);
    decision.ShouldBeError(new ForbiddenError());
  }
}
