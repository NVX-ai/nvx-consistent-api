namespace Nvx.ConsistentAPI;

public static class Matchers
{
  private static Func<Result<EventInsertion, ApiError>> NotFound<T>(
    this EventModelCommand<T> self,
    Option<UserSecurity> user)
    where T : EventModelEntity<T> =>
    () => new NotFoundError(
      typeof(T).Name,
      self.TryGetEntityId(user).Map(eid => eid.StreamId()).DefaultValue("Unknown"));

  private static Func<Result<EventInsertion, ApiError>> NotFound<T>(
    this TenantEventModelCommand<T> self,
    UserSecurity user,
    Guid tenantId
  ) where T : EventModelEntity<T> =>
    () => new NotFoundError(
      typeof(T).Name,
      self.TryGetEntityId(user, tenantId).Map(eid => eid.StreamId()).DefaultValue("Unknown"));

  private static Func<T, Result<EventInsertion, ApiError>> AlreadyExisting<T>()
    where T : EventModelEntity<T> =>
    _ => new ConflictError($"Tried to create {typeof(T).Name} when it already existed.");

  public static Result<EventInsertion, ApiError> ShouldCreate<T>(
    this TenantEventModelCommand<T> _,
    Option<T> entity,
    Func<Result<EventModelEvent[], ApiError>> fn
  ) where T : EventModelEntity<T> =>
    entity.Match(AlreadyExisting<T>(), () => fn().Map<EventInsertion>(evts => new CreateStream(evts)));

  public static Result<EventInsertion, ApiError> ShouldCreate<T>(
    this EventModelCommand<T> _,
    Option<T> entity,
    Func<Result<EventModelEvent[], ApiError>> fn
  ) where T : EventModelEntity<T> =>
    entity.Match(AlreadyExisting<T>(), () => fn().Map<EventInsertion>(evts => new CreateStream(evts)));

  public static Result<EventInsertion, ApiError> ShouldCreate<T>(
    this EventModelCommand<T> _,
    Option<T> entity,
    EventModelEvent evt
  ) where T : EventModelEntity<T> => entity.Match(AlreadyExisting<T>(), () => new CreateStream(evt));

  public static Result<EventInsertion, ApiError> ShouldCreate<T>(
    this EventModelCommand<T> _,
    Option<T> entity,
    Option<UserSecurity> user,
    Func<UserSecurity, Result<EventModelEvent[], ApiError>> fn
  ) where T : EventModelEntity<T> =>
    user
      .Match<Result<UserSecurity, ApiError>>(u => u, () => new UnauthorizedError())
      .Bind(u =>
        entity.Match(
          AlreadyExisting<T>(),
          () => fn(u).Map<EventInsertion>(evts => new CreateStream(evts))
        )
      );

  public static Result<EventInsertion, ApiError> Require<T>(
    this TenantEventModelCommand<T> self,
    Option<T> entity,
    UserSecurity user,
    Guid tenantId,
    Func<T, Result<ExistingStreamInsertion, ApiError>> fn
  ) where T : EventModelEntity<T> => entity.Match(e => fn(e).Map<EventInsertion>(Id), self.NotFound(user, tenantId));

  public static Result<EventInsertion, ApiError> Require<T>(
    this EventModelCommand<T> self,
    Option<T> entity,
    Option<UserSecurity> user,
    Func<T, UserSecurity, Result<ExistingStreamInsertion, ApiError>> fn
  ) where T : EventModelEntity<T> =>
    user
      .Match<Result<UserSecurity, ApiError>>(u => u, () => new UnauthorizedError())
      .Bind(u => entity.Match(e => fn(e, u).Map<EventInsertion>(Id), self.NotFound(user)));

  public static Result<EventInsertion, ApiError> Require<T>(
    this EventModelCommand<T> self,
    Option<T> entity,
    Option<UserSecurity> user,
    EventModelEvent evt
  ) where T : EventModelEntity<T> =>
    user
      .Match<Result<UserSecurity, ApiError>>(u => u, () => new UnauthorizedError())
      .Bind(_ => entity.Match(_ => new AnyState(evt), self.NotFound(user)));

  public static Result<EventInsertion, ApiError> Require<T>(
    this EventModelCommand<T> self,
    Option<T> entity,
    Func<T, Result<ExistingStreamInsertion, ApiError>> fn
  ) where T : EventModelEntity<T> =>
    entity.Match(e => fn(e).Map<EventInsertion>(Id), self.NotFound(None));

  public static Result<EventInsertion, ApiError> Require<T>(
    this EventModelCommand<T> _,
    Option<UserSecurity> user,
    Func<UserSecurity, Result<ExistingStreamInsertion, ApiError>> fn
  ) where T : EventModelEntity<T> =>
    user.Match(u => fn(u).Map<EventInsertion>(Id), () => new UnauthorizedError());
}
