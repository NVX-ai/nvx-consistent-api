namespace Nvx.ConsistentAPI.Tests.UserNotifications.ReadModels;

public class UserNotificationReadModelShould
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

    var baseExpectation = new UserNotificationReadModel(
      notificationId,
      recipient.Sub,
      message,
      messageType,
      relatedEntityId,
      relatedEntityType,
      sender.Sub,
      false,
      false,
      false,
      null,
      entity.CreatedAt,
      []
    );

    UserNotificationReadModel
      .FromEntity(entity)
      .ShouldBeSingle(baseValue =>
      {
        Assert.Equal(baseExpectation.Id, baseValue.Id);
        Assert.Equal(baseExpectation.UserSub, baseValue.UserSub);
        Assert.Equal(baseExpectation.Message, baseValue.Message);
        Assert.Equal(baseExpectation.MessageType, baseValue.MessageType);
        Assert.Equal(baseExpectation.RelatedEntityId, baseValue.RelatedEntityId);
        Assert.Equal(baseExpectation.RelatedEntityType, baseValue.RelatedEntityType);
        Assert.Equal(baseExpectation.SenderSub, baseValue.SenderSub);
        Assert.Equal(baseExpectation.CreatedAt, baseValue.CreatedAt);
      });

    var secondExpectation = new UserNotificationReadModel(
      notificationId,
      recipient.Sub,
      message,
      messageType,
      relatedEntityId,
      relatedEntityType,
      sender.Sub,
      true,
      false,
      false,
      null,
      entity.CreatedAt,
      []
    );

    UserNotificationReadModel
      .FromEntity(
        await entity.Fold(
          new NotificationRead(notificationId),
          new EventMetadata(DateTime.UtcNow, null, null, null, null, null),
          null!))
      .ShouldBeSingle(baseValue =>
      {
        Assert.Equal(secondExpectation.Id, baseValue.Id);
        Assert.Equal(secondExpectation.UserSub, baseValue.UserSub);
        Assert.Equal(secondExpectation.Message, baseValue.Message);
        Assert.Equal(secondExpectation.MessageType, baseValue.MessageType);
        Assert.Equal(secondExpectation.RelatedEntityId, baseValue.RelatedEntityId);
        Assert.Equal(secondExpectation.RelatedEntityType, baseValue.RelatedEntityType);
        Assert.Equal(secondExpectation.SenderSub, baseValue.SenderSub);
        Assert.Equal(secondExpectation.CreatedAt, baseValue.CreatedAt);
      });

    var thirdExpectation = new UserNotificationReadModel(
      notificationId,
      recipient.Sub,
      message,
      messageType,
      relatedEntityId,
      relatedEntityType,
      sender.Sub,
      false,
      false,
      false,
      null,
      entity.CreatedAt,
      []
    );

    UserNotificationReadModel
      .FromEntity(
        await entity.Fold(
          new NotificationUnread(notificationId),
          new EventMetadata(DateTime.UtcNow, null, null, null, null, null),
          null!))
      .ShouldBeSingle(baseValue =>
      {
        Assert.Equal(thirdExpectation.Id, baseValue.Id);
        Assert.Equal(thirdExpectation.UserSub, baseValue.UserSub);
        Assert.Equal(thirdExpectation.Message, baseValue.Message);
        Assert.Equal(thirdExpectation.MessageType, baseValue.MessageType);
        Assert.Equal(thirdExpectation.RelatedEntityId, baseValue.RelatedEntityId);
        Assert.Equal(thirdExpectation.RelatedEntityType, baseValue.RelatedEntityType);
        Assert.Equal(thirdExpectation.SenderSub, baseValue.SenderSub);
        Assert.Equal(thirdExpectation.CreatedAt, baseValue.CreatedAt);
      });

    var fourthExpectation = new UserNotificationReadModel(
      notificationId,
      recipient.Sub,
      message,
      messageType,
      relatedEntityId,
      relatedEntityType,
      sender.Sub,
      true,
      true,
      false,
      null,
      entity.CreatedAt,
      []
    );

    UserNotificationReadModel
      .FromEntity(
        await entity.Fold(
          new NotificationArchived(notificationId),
          new EventMetadata(DateTime.UtcNow, null, null, null, null, null),
          null!))
      .ShouldBeSingle(baseValue =>
      {
        Assert.Equal(fourthExpectation.Id, baseValue.Id);
        Assert.Equal(fourthExpectation.UserSub, baseValue.UserSub);
        Assert.Equal(fourthExpectation.Message, baseValue.Message);
        Assert.Equal(fourthExpectation.MessageType, baseValue.MessageType);
        Assert.Equal(fourthExpectation.RelatedEntityId, baseValue.RelatedEntityId);
        Assert.Equal(fourthExpectation.RelatedEntityType, baseValue.RelatedEntityType);
        Assert.Equal(fourthExpectation.SenderSub, baseValue.SenderSub);
        Assert.Equal(fourthExpectation.CreatedAt, baseValue.CreatedAt);
      });

    Assert.Empty(
      UserNotificationReadModel.FromEntity(
        await entity.Fold(
          new NotificationDeleted(notificationId),
          new EventMetadata(DateTime.UtcNow, null, null, null, null, null),
          null!)));

    var fifthExpectation = new UserNotificationReadModel(
      notificationId,
      recipient.Sub,
      message,
      messageType,
      relatedEntityId,
      relatedEntityType,
      sender.Sub,
      false,
      false,
      false,
      null,
      entity.CreatedAt,
      []
    );

    UserNotificationReadModel
      .FromEntity(
        await entity.Fold(
          new NotificationRestored(notificationId),
          new EventMetadata(DateTime.UtcNow, null, null, null, null, null),
          null!))
      .ShouldBeSingle(baseValue =>
      {
        Assert.Equal(fifthExpectation.Id, baseValue.Id);
        Assert.Equal(fifthExpectation.UserSub, baseValue.UserSub);
        Assert.Equal(fifthExpectation.Message, baseValue.Message);
        Assert.Equal(fifthExpectation.MessageType, baseValue.MessageType);
        Assert.Equal(fifthExpectation.RelatedEntityId, baseValue.RelatedEntityId);
        Assert.Equal(fifthExpectation.RelatedEntityType, baseValue.RelatedEntityType);
        Assert.Equal(fifthExpectation.SenderSub, baseValue.SenderSub);
        Assert.Equal(fifthExpectation.CreatedAt, baseValue.CreatedAt);
      });
  }
}
