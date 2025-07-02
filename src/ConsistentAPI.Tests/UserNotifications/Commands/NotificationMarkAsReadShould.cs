namespace ConsistentAPI.Tests.UserNotifications.Commands;

public class NotificationMarkAsReadShould
{
  [Fact(DisplayName = "Mark notification as read")]
  public async Task MarkNotificationAsRead()
  {
    var user = TestData.UserWithNoPermissions();
    var notificationId = Guid.NewGuid().ToString();
    var userSub = user.Sub;
    var message = Guid.NewGuid().ToString();
    var messageType = Guid.NewGuid().ToString();
    var relatedEntityId = Guid.NewGuid().ToString();
    var relatedEntityType = Guid.NewGuid().ToString();

    var entity = await UserNotificationEntity
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
        new EventMetadata(DateTime.UtcNow, null, null, null, null),
        null!);
    Assert.False(entity.IsRead);
    var decision = new NotificationMarkAsRead(notificationId).Decide(entity, user, []);
    decision.ShouldBeOk(ei =>
    {
      Assert.True(ei is AnyState);
      Assert.Single(((AnyState)ei).Events);
      Assert.Equal(new NotificationRead(notificationId), ((AnyState)ei).Events[0]);
      return unit;
    });
  }

  [Fact(DisplayName = "Mark notification as unread")]
  public async Task MarkNotificationAsUnread()
  {
    var user = TestData.UserWithNoPermissions();
    var notificationId = Guid.NewGuid().ToString();
    var userSub = user.Sub;
    var message = Guid.NewGuid().ToString();
    var messageType = Guid.NewGuid().ToString();
    var relatedEntityId = Guid.NewGuid().ToString();
    var relatedEntityType = Guid.NewGuid().ToString();

    var entity = await UserNotificationEntity
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
        new EventMetadata(DateTime.UtcNow, null, null, null, null),
        null!);
    Assert.False(entity.IsRead);
    var decision = new NotificationMarkAsUnread(notificationId).Decide(entity, user, []);
    decision.ShouldBeOk(ei =>
    {
      Assert.True(ei is AnyState);
      Assert.Single(((AnyState)ei).Events);
      Assert.Equal(new NotificationUnread(notificationId), ((AnyState)ei).Events[0]);
      return unit;
    });
  }

  [Fact(DisplayName = "Forbid other users")]
  public async Task ForbidsOthers()
  {
    var user = TestData.UserWithNoPermissions();
    var notificationId = Guid.NewGuid().ToString();
    var userSub = Guid.NewGuid().ToString();
    var message = Guid.NewGuid().ToString();
    var messageType = Guid.NewGuid().ToString();
    var relatedEntityId = Guid.NewGuid().ToString();
    var relatedEntityType = Guid.NewGuid().ToString();

    var entity = await UserNotificationEntity
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
        new EventMetadata(DateTime.UtcNow, null, null, null, null),
        null!);
    var command = new NotificationMarkAsRead(notificationId);
    Assert.Equal(new StrongString(notificationId), command.TryGetEntityId(user));
    var decision = command.Decide(entity, user, []);
    decision.ShouldBeError(new ForbiddenError());
  }
}
