namespace Nvx.ConsistentAPI.Tests.Framework.Notifications;

public class NotificationTests
{
  [Fact(DisplayName = "emits notifications")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var message = Guid.NewGuid().ToString();
    await setup.Command(new SendNotificationToUser(message, setup.Auth.ByName("john")), true);
    var notifications = await setup.ReadModels<UserNotificationReadModel>(
      asUser: "john",
      waitType: ConsistencyWaitType.Long);
    Assert.Single(notifications.Items);
    var notification = notifications.Items.First();
    Assert.Equal(message, notification.Message);
    Assert.False(notification.IsRead);
    Assert.Equal("banana", notification.AdditionalDetails.GetValueOrDefault("banana"));
  }
}
