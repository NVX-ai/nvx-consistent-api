using System.Collections.Concurrent;
using Nvx.ConsistentAPI.Framework;

namespace Nvx.ConsistentAPI;

public record ConcernedEntityReceivedInterest(
  string ConcernedEntityStreamName,
  Dictionary<string, string> ConcernedEntityId,
  string InterestedEntityStreamName,
  Dictionary<string, string> InterestedEntityId,
  string OriginatingEventId)
  : EventModelEvent
{
  public StrongId GetEntityId() => new ConcernedEntityId(ConcernedEntityStreamName);
  public string SwimLane => ConcernedEntityEntity.StreamPrefix;
}

public record ConcernedEntityHadInterestRemoved(
  string ConcernedEntityStreamName,
  Dictionary<string, string> ConcernedEntityId,
  string InterestedEntityStreamName,
  Dictionary<string, string> InterestedEntityId,
  string OriginatingEventId)
  : EventModelEvent
{
  public StrongId GetEntityId() => new ConcernedEntityId(ConcernedEntityStreamName);
  public string SwimLane => ConcernedEntityEntity.StreamPrefix;
}

public record InterestedEntityRegisteredInterest(
  string InterestedEntityStreamName,
  Dictionary<string, string> InterestedEntityId,
  string ConcernedEntityStreamName,
  Dictionary<string, string> ConcernedEntityId,
  string OriginatingEventId)
  : EventModelEvent
{
  public StrongId GetEntityId() => new InterestedEntityId(InterestedEntityStreamName);
  public string SwimLane => InterestedEntityEntity.StreamPrefix;
}

public record InterestedEntityHadInterestRemoved(
  string InterestedEntityStreamName,
  Dictionary<string, string> InterestedEntityId,
  string ConcernedEntityStreamName,
  Dictionary<string, string> ConcernedEntityId,
  string OriginatingEventId)
  : EventModelEvent
{
  public StrongId GetEntityId() => new InterestedEntityId(InterestedEntityStreamName);
  public string SwimLane => InterestedEntityEntity.StreamPrefix;
}

internal static class EntityIdExtensions
{
  private static readonly ConcurrentDictionary<string, Type> TypeCache = new();

  public static Option<StrongId> GetStrongId(this Dictionary<string, string> self)
  {
    try
    {
      return
        from type in self.GetIdType()
        from body in self.GetIdBody()
        from id in Optional(Serialization.Deserialize(type, body) as StrongId)
        select id;
    }
    catch
    {
      return None;
    }
  }

  private static Option<Type> GetIdType(this Dictionary<string, string> dictionary)
  {
    try
    {
      if (!dictionary.TryGetValue("StrongIdTypeName", out var typeName))
      {
        return None;
      }

      var cacheKey = dictionary.TryGetValue("StrongIdTypeNamespace", out var typeNamespace)
        ? $"{typeNamespace}.{typeName}"
        : typeName;

      if (TypeCache.TryGetValue(cacheKey, out var cachedType))
      {
        return Optional(cachedType);
      }

      foreach (var foundType in FindTypeByName(cacheKey))
      {
        TypeCache.TryAdd(cacheKey, foundType);
        return foundType;
      }

      return None;
    }
    catch
    {
      // ignored
    }

    return None;
  }

  private static Option<Type> FindTypeByName(string fullName)
  {
    var directType = Type.GetType(fullName);
    if (directType != null)
    {
      return directType;
    }

    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
      try
      {
        var fromAssembly = assembly.GetType(fullName);
        if (fromAssembly != null)
        {
          return fromAssembly;
        }
      }
      catch
      {
        // Skip assemblies that can't be loaded or reflect over
      }
    }

    return None;
  }

  private static Option<string> GetIdBody(this Dictionary<string, string> dictionary) =>
    dictionary.TryGetValue("SerializedId", out var id) ? id : None;
}
