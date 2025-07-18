﻿namespace Nvx.ConsistentAPI;

public record NotificationSent(
  string Id,
  string UserSub,
  string Message,
  string MessageType,
  string? RelatedEntityId,
  string? RelatedEntityType,
  string? SenderSub,
  DateTime CreatedAt,
  Dictionary<string, string>? AdditionalDetails) : EventModelEvent
{
  public string GetStreamName() => UserNotificationEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}

public record NotificationRead(string Id) : EventModelEvent
{
  public string GetStreamName() => UserNotificationEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}

public record NotificationUnread(string Id) : EventModelEvent
{
  public string GetStreamName() => UserNotificationEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}

public record NotificationFavorite(string Id) : EventModelEvent
{
  public string GetStreamName() => UserNotificationEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}

public record NotificationUnfavorite(string Id) : EventModelEvent
{
  public string GetStreamName() => UserNotificationEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}

public record NotificationArchived(string Id) : EventModelEvent
{
  public string GetStreamName() => UserNotificationEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}

public record NotificationDeleted(string Id) : EventModelEvent
{
  public string GetStreamName() => UserNotificationEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}

public record NotificationRestored(string Id) : EventModelEvent
{
  public string GetStreamName() => UserNotificationEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}

public record NotificationPermanentlyDeleted(string Id) : EventModelEvent
{
  public string GetStreamName() => UserNotificationEntity.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongString(Id);
}
