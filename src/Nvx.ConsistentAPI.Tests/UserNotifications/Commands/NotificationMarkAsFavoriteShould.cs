namespace Nvx.ConsistentAPI.Tests.UserNotifications.Commands;

public class NotificationMarkAsFavoriteShould
{
  [Fact(DisplayName = "Mark notification as favorite")]
  public async Task MarkNotificationAsFavorite()
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
    Assert.False(entity.IsFavorite);
    var decision = new NotificationMarkAsFavorite(notificationId).Decide(entity, user, []);
    decision.ShouldBeOk(ei =>
    {
      Assert.True(ei is AnyState);
      Assert.Single(((AnyState)ei).Events);
      Assert.Equal(new NotificationFavorite(notificationId), ((AnyState)ei).Events[0]);
      return unit;
    });
  }

  [Fact(DisplayName = "Mark notification as unfavorite")]
  public async Task MarkNotificationAsUnfavorite()
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
    Assert.False(entity.IsFavorite);
    var decision = new NotificationMarkAsUnfavorite(notificationId).Decide(entity, user, []);
    decision.ShouldBeOk(ei =>
    {
      Assert.True(ei is AnyState);
      Assert.Single(((AnyState)ei).Events);
      Assert.Equal(new NotificationUnfavorite(notificationId), ((AnyState)ei).Events[0]);
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
    var command = new NotificationMarkAsFavorite(notificationId);
    Assert.Equal(new StrongString(notificationId), command.TryGetEntityId(user));
    var decision = command.Decide(entity, user, []);
    decision.ShouldBeError(new ForbiddenError());
  }
}
