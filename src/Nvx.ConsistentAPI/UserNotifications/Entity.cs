namespace Nvx.ConsistentAPI;

public enum UserNotificationState
{
  Inactive = 0,
  Active = 1,
  SoftDeleted = 2,
  Deleted = 3
}

public partial record UserNotificationEntity(
  string Id,
  string UserSub,
  string Message,
  string MessageType,
  string? RelatedEntityId,
  string? RelatedEntityType,
  string? SenderSub,
  bool IsRead,
  bool IsArchived,
  UserNotificationState State,
  bool IsFavorite,
  DateTime? DeletedAt,
  DateTime CreatedAt,
  Dictionary<string, string> AdditionalDetails) :
  EventModelEntity<UserNotificationEntity>,
  Folds<NotificationSent, UserNotificationEntity>,
  Folds<NotificationRead, UserNotificationEntity>,
  Folds<NotificationUnread, UserNotificationEntity>,
  Folds<NotificationFavorite, UserNotificationEntity>,
  Folds<NotificationUnfavorite, UserNotificationEntity>,
  Folds<NotificationArchived, UserNotificationEntity>,
  Folds<NotificationDeleted, UserNotificationEntity>,
  Folds<NotificationRestored, UserNotificationEntity>,
  Folds<NotificationPermanentlyDeleted, UserNotificationEntity>
{
  public const string StreamPrefix = "user-notification-";

  public static readonly EntityDefinition Definition =
    new EntityDefinition<UserNotificationEntity, StrongString> { Defaulter = Defaulted, StreamPrefix = StreamPrefix };

  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<UserNotificationEntity> Fold(
    NotificationArchived evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { IsArchived = true, IsRead = true, IsFavorite = false });

  public ValueTask<UserNotificationEntity> Fold(
    NotificationDeleted evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        State = UserNotificationState.SoftDeleted, IsRead = true, DeletedAt = metadata.CreatedAt, IsFavorite = false
      });

  public ValueTask<UserNotificationEntity> Fold(
    NotificationFavorite evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { IsFavorite = true });

  public ValueTask<UserNotificationEntity> Fold(
    NotificationPermanentlyDeleted evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { State = UserNotificationState.Deleted });

  public ValueTask<UserNotificationEntity> Fold(
    NotificationRead evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { IsRead = true });

  public ValueTask<UserNotificationEntity> Fold(
    NotificationRestored evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { State = UserNotificationState.Active, IsArchived = false, DeletedAt = null });

  public ValueTask<UserNotificationEntity> Fold(
    NotificationSent evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult<UserNotificationEntity>(
      new UserNotificationEntity(
        evt.Id,
        evt.UserSub,
        evt.Message,
        evt.MessageType,
        evt.RelatedEntityId,
        evt.RelatedEntityType,
        evt.SenderSub,
        false,
        false,
        UserNotificationState.Active,
        false,
        null,
        evt.CreatedAt,
        evt.AdditionalDetails ?? []));

  public ValueTask<UserNotificationEntity> Fold(
    NotificationUnfavorite evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { IsFavorite = false });

  public ValueTask<UserNotificationEntity> Fold(
    NotificationUnread evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { IsRead = false });

  public static string GetStreamName(string id) => $"{StreamPrefix}{id}";

  public static UserNotificationEntity Defaulted(StrongString id) =>
    new(
      id.Value,
      "",
      "",
      "",
      null,
      null,
      null,
      false,
      false,
      UserNotificationState.Inactive,
      false,
      null,
      DateTime.MinValue,
      []);
}
