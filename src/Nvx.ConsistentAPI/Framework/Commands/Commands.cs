namespace Nvx.ConsistentAPI;

public interface EventModelCommand<Entity> : EventModelCommand where Entity : EventModelEntity<Entity>
{
  Result<EventInsertion, ApiError> Decide(Option<Entity> entity, Option<UserSecurity> user, FileUpload[] files);
  Option<StrongId> TryGetEntityId(Option<UserSecurity> user);
}

public interface TenantEventModelCommand<Entity> : EventModelCommand where Entity : EventModelEntity<Entity>
{
  Result<EventInsertion, ApiError> Decide(Guid tenantId, Option<Entity> entity, UserSecurity user, FileUpload[] files);
  Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId);
}

public interface EventModelCommand
{
  public Result<EventInsertion, ApiError> Decide<Entity>(
    Option<Entity> entity,
    UserSecurity? user,
    FileUpload[] files,
    Guid? tenantId = null
  ) where Entity : EventModelEntity<Entity> =>
    this switch
    {
      TenantEventModelCommand<Entity> tc => tenantId.HasValue && user != null
        ? tc.Decide(tenantId.Value, entity, user, files)
        : new DisasterError("A tenancy command requires a tenant ID and an authenticated user"),
      EventModelCommand<Entity> c => c.Decide(entity, Optional(user), files),
      _ => new DisasterError("This command definition is incomplete.")
    };

  public IEnumerable<string> Validate() => [];
}

public record CommandAcceptedResult(string EntityId);

public record CacheLockedResult;

public record SuccessCachedResult(CommandAcceptedResult Value);

public record ErrorCacheResult(ApiError Value);

public record CacheLockAvailableResult(long Revision);
