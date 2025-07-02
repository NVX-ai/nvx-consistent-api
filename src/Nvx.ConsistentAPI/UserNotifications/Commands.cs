namespace Nvx.ConsistentAPI;

public record NotificationMarkAsRead(string Id) : EventModelCommand<UserNotificationEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<UserNotificationEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    this.Require(
      entity,
      user,
      (un, u) => u.Sub == un.UserSub ? new AnyState(new NotificationRead(Id)) : new ForbiddenError()
    );

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
}

public record NotificationMarkAsUnread(string Id) : EventModelCommand<UserNotificationEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<UserNotificationEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    this.Require(
      entity,
      user,
      (un, u) => u.Sub == un.UserSub ? new AnyState(new NotificationUnread(Id)) : new ForbiddenError()
    );

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
}

public record NotificationMarkAsFavorite(string Id) : EventModelCommand<UserNotificationEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<UserNotificationEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    this.Require(
      entity,
      user,
      (un, u) => u.Sub == un.UserSub ? new AnyState(new NotificationFavorite(Id)) : new ForbiddenError()
    );

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
}

public record NotificationMarkAsUnfavorite(string Id) : EventModelCommand<UserNotificationEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<UserNotificationEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    this.Require(
      entity,
      user,
      (un, u) => u.Sub == un.UserSub ? new AnyState(new NotificationUnfavorite(Id)) : new ForbiddenError()
    );

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
}

public record NotificationArchive(string Id) : EventModelCommand<UserNotificationEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<UserNotificationEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    this.Require(
      entity,
      user,
      (un, u) => u.Sub == un.UserSub ? new AnyState(new NotificationArchived(Id)) : new ForbiddenError()
    );

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
}

public record NotificationDelete(string Id) : EventModelCommand<UserNotificationEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<UserNotificationEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    this.Require(
      entity,
      user,
      (un, u) => u.Sub == un.UserSub ? new AnyState(new NotificationDeleted(Id)) : new ForbiddenError()
    );

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
}

public record NotificationRestore(string Id) : EventModelCommand<UserNotificationEntity>
{
  public Result<EventInsertion, ApiError> Decide(
    Option<UserNotificationEntity> entity,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    this.Require(
      entity,
      user,
      (un, u) => u.Sub == un.UserSub ? new AnyState(new NotificationRestored(Id)) : new ForbiddenError()
    );

  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
}
