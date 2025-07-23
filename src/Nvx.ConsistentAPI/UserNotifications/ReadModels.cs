using System.ComponentModel.DataAnnotations;
using Nvx.ConsistentAPI.Framework;

namespace Nvx.ConsistentAPI;

public record UserNotificationReadModel(
  string Id,
  string UserSub,
  [property: MaxLength(StringSizes.Message)]
  string Message,
  string MessageType,
  string? RelatedEntityId,
  string? RelatedEntityType,
  string? SenderSub,
  bool IsRead,
  bool IsArchived,
  bool IsFavorite,
  DateTime? DeletedAt,
  DateTime CreatedAt,
  Dictionary<string, string> AdditionalDetails) : EventModelReadModel, UserBound
{
  public StrongId GetStrongId() => new StrongString(Id);

  public static UserNotificationReadModel[] FromEntity(UserNotificationEntity entity) =>
    entity.State == UserNotificationState.Active
      ?
      [
        new UserNotificationReadModel(
          entity.Id,
          entity.UserSub,
          entity.Message,
          entity.MessageType,
          entity.RelatedEntityId,
          entity.RelatedEntityType,
          entity.SenderSub,
          entity.IsRead,
          entity.IsArchived,
          entity.IsFavorite,
          entity.DeletedAt,
          entity.CreatedAt,
          entity.AdditionalDetails)
      ]
      : [];
}

public record UserNotificationDeletedReadModel(
  string Id,
  string UserSub,
  [property: MaxLength(StringSizes.Message)]
  string Message,
  string MessageType,
  string? RelatedEntityId,
  string? RelatedEntityType,
  string? SenderSub,
  bool IsRead,
  bool IsArchived,
  bool IsFavorite,
  DateTime? DeletedAt,
  DateTime CreatedAt,
  Dictionary<string, string> AdditionalDetails) : EventModelReadModel, UserBound
{
  public StrongId GetStrongId() => new StrongString(Id);

  public static UserNotificationDeletedReadModel[] FromEntity(UserNotificationEntity entity) =>
    entity.State == UserNotificationState.SoftDeleted
      ?
      [
        new UserNotificationDeletedReadModel(
          entity.Id,
          entity.UserSub,
          entity.Message,
          entity.MessageType,
          entity.RelatedEntityId,
          entity.RelatedEntityType,
          entity.SenderSub,
          entity.IsRead,
          entity.IsArchived,
          entity.IsFavorite,
          entity.DeletedAt,
          entity.CreatedAt,
          entity.AdditionalDetails)
      ]
      : [];
}
