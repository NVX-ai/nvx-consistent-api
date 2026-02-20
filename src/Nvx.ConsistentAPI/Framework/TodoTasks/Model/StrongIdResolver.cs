using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Nvx.ConsistentAPI;

/// <summary>
/// Resolves the strongly-typed entity ID for a todo from its serialized or string representation,
/// based on the task definition's <see cref="TodoTaskDefinition.EntityIdType"/>.
/// </summary>
internal static class StrongIdResolver
{
  private static readonly Type UserWithTenantPermissionIdType = typeof(UserWithTenantPermissionId);
  private static readonly Type UserWithPermissionIdType = typeof(UserWithPermissionId);
  private static readonly Type StrongStringType = typeof(StrongString);
  private static readonly Type StrongGuidType = typeof(StrongGuid);

  internal static StrongId Resolve(
    TodoEventModelReadModel todo,
    TodoTaskDefinition definition,
    ILogger logger)
  {
    if (!string.IsNullOrEmpty(todo.SerializedRelatedEntityId))
    {
      try
      {
        return (StrongId)JsonConvert.DeserializeObject(
          todo.SerializedRelatedEntityId!,
          definition.EntityIdType)!;
      }
      catch (Exception ex)
      {
        logger.LogError(
          ex,
          "Failed deserializing {SerializedRelatedEntityId}\nfor task {Todo}\nfor definition {Definition}",
          todo.SerializedRelatedEntityId,
          todo,
          definition);
        return new StrongString(todo.RelatedEntityId);
      }
    }

    if (StrongGuidType == definition.EntityIdType)
    {
      return new StrongGuid(Guid.TryParse(todo.RelatedEntityId, out var id) ? id : Guid.NewGuid());
    }

    if (StrongStringType == definition.EntityIdType)
    {
      return new StrongString(todo.RelatedEntityId);
    }

    if (UserWithPermissionIdType == definition.EntityIdType)
    {
      try
      {
        return new UserWithPermissionId(
          todo.RelatedEntityId.Split("#")[0],
          todo.RelatedEntityId.Split("#")[1]);
      }
      catch (Exception ex)
      {
        logger.LogError(
          ex,
          "Failed deserializing UserWithPermissionId: {RelatedEntityId}\nfor task {Todo}\nfor definition {Definition}",
          todo.RelatedEntityId,
          todo,
          definition);
        return new StrongString(todo.RelatedEntityId);
      }
    }

    if (UserWithTenantPermissionIdType == definition.EntityIdType)
    {
      try
      {
        return new UserWithTenantPermissionId(
          todo.RelatedEntityId.Split("#")[0],
          Guid.Parse(todo.RelatedEntityId.Split("#")[1]),
          todo.RelatedEntityId.Split("#")[2]
        );
      }
      catch (Exception ex)
      {
        logger.LogError(
          ex,
          "Failed deserializing UserWithTenantPermissionId: {RelatedEntityId}\nfor task {Todo}\nfor definition {Definition}",
          todo.RelatedEntityId,
          todo,
          definition);
      }
    }

    return new StrongString(todo.RelatedEntityId);
  }
}
