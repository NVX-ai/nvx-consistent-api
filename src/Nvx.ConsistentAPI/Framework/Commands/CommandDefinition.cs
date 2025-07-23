using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.EventStore.Events;
using Nvx.ConsistentAPI.Framework;

namespace Nvx.ConsistentAPI;

/// <summary>
///   Command artifact, includes the metadata needed for the framework to wire the endpoint
///   and execute validations and additional checks.
/// </summary>
/// <typeparam name="Shape">Command contract.</typeparam>
/// <typeparam name="Entity">Type of the entity the command makes a decision about</typeparam>
public class CommandDefinition<Shape, Entity> : EventModelingCommandArtifact
  where Entity : EventModelEntity<Entity>
  where Shape : EventModelCommand
{
  private readonly PropertyInfo[] fileArrayProperties =
    typeof(Shape)
      .GetProperties()
      .Where(p =>
        p.PropertyType.IsArray
          ? p.PropertyType.GetElementType() == typeof(AttachedFile)
          : p
            .PropertyType.GetInterfaces()
            .Any(i =>
              i.IsGenericType
              && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
              && i.GenericTypeArguments[0] == typeof(AttachedFile)
            )
      )
      .ToArray();

  private readonly PropertyInfo[] fileProperties =
    typeof(Shape).GetProperties().Where(p => p.PropertyType == typeof(AttachedFile)).ToArray();

  private readonly string[] invalidIdempotencyKeys = ["undefined", "", "null", "nan", "nil", "none", "empty"];
  private readonly string routeSegment = Naming.ToSpinalCase<Shape>();

  /// <summary>
  ///   Description to be shown in the API specification.
  /// </summary>
  public Option<string> Description { private get; init; } = None;

  /// <summary>
  ///   Special authorization logic for this command, it is recommended to document them in the
  ///   <see cref="Description" /> property.
  /// </summary>
  public CustomAuthorization<Entity, Shape>? CustomAuthorization { private get; init; }

  /// <summary>
  ///   Interceptor of the OpenApi specification builder for the operation, use with care,
  ///   as the framework does not introspect actions performed by it.
  /// </summary>
  public Action<OpenApiOperation> OpenApiCustomizer { private get; init; } = _ => { };

  /// <summary>
  ///   Used to group endpoints in the API, it's expected to be the name of the business capability
  ///   the endpoint belongs to, so definitions belonging to different entities
  /// </summary>
  public required string AreaTag { private get; init; }

  /// <summary>
  ///   Whether it would use the JsonLogic validation rules.
  ///   This is not documented by the framework automatically, so it is recommended to document it in the
  ///   <see cref="Description" /> property.
  /// </summary>
  public bool UsesValidationRules { get; init; }

  public AuthOptions Auth { get; init; } = new Everyone();

  public void ApplyTo(WebApplication app, Fetcher fetcher, Emitter emitter, GeneratorSettings settings, ILogger logger)
  {
    if (!settings.EnabledFeatures.HasCommands())
    {
      return;
    }

    Delegate handler =
      async (HttpContext context) =>
      {
        try
        {
          await Process(context, None).Async().Apply(Respond(context));
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed issuing command of type {Shape}", typeof(Shape).Name);
          throw;
        }
      };
    Delegate tenantHandler =
      async (HttpContext context, Guid tenantId) =>
      {
        try
        {
          await Process(context, tenantId).Async().Apply(Respond(context));
        }
        catch (Exception e)
        {
          logger.LogError(e, "Failed issuing command of type {Shape}", typeof(Shape).Name);
          throw;
        }
      };

    var isTenantBound = typeof(Shape).GetInterfaces().Any(i => i == typeof(TenantEventModelCommand<Entity>));

    var builder = isTenantBound
      ? app.MapPost($"/tenant/{{tenantId:Guid}}/commands/{routeSegment}", tenantHandler)
      : app.MapPost($"/commands/{routeSegment}", handler);

    builder
      .Accepts<Shape>(false, "application/json")
      .Produces<CommandAcceptedResult>()
      .Produces<ErrorResponse>(500)
      .Produces<ErrorResponse>(400)
      .Produces<ErrorResponse>(409)
      .WithOpenApi(o =>
      {
        o.Description = Description.DefaultValue(o.Description);
        o.Parameters ??= new List<OpenApiParameter>();
        o.Parameters.Add(
          new OpenApiParameter
          {
            Name = "IdempotencyKey",
            In = ParameterLocation.Header,
            Description =
              "Commands issued with this key will conflict with another command issued with the same key at the same time, once the first command with this key is executed, subsequent calls to this command will return the stored result",
            Required = false,
            Schema = new OpenApiSchema { Type = "string" }
          });
        o.OperationId = typeof(Shape).Name;
        if (!string.IsNullOrWhiteSpace(AreaTag))
        {
          o.Tags = [new OpenApiTag { Name = AreaTag }];
        }

        OpenApiCustomizer(o);
        return o;
      });

    builder.ApplyAuth(Auth);

    return;

    Func<AsyncResult<CommandAcceptedResult, ApiError>, Task> Respond(HttpContext context) =>
      r => r.Match(
        async car =>
        {
          context.Response.StatusCode = StatusCodes.Status200OK;
          await context.Response.WriteAsJsonAsync(car);
        },
        async err =>
        {
          switch (err)
          {
            case DisasterError e:
              logger.LogError("{Error}", e);
              break;
            case CorruptStreamError e:
              logger.LogError("{Error}", e);
              break;
          }

          await err.Respond(context);
        });

    async Task<Result<FileUpload[], ApiError>> ValidateFiles(Shape command)
    {
      var attachedFiles =
        (
          from fp in fileProperties
          let af = (AttachedFile?)fp.GetValue(command)
          where af != null
          select af
        )
        .Concat(
          from fap in fileArrayProperties
          from af in (IEnumerable<AttachedFile>)fap.GetValue(command)!
          select af
        )
        .ToArray();


      if (attachedFiles.Length == 0)
      {
        return Array.Empty<FileUpload>();
      }

      var files =
        await attachedFiles
          .Select(af => Some<StrongId>(new StrongGuid(af.Id)))
          .Select<Option<StrongId>, Func<Task<FetchResult<FileUpload>>>>(fid => () => fetcher.Fetch<FileUpload>(fid))
          .Parallel();

      if (!files.All(IsAttachableFile))
      {
        return new ConflictError("Some of the attached files were not found in the system");
      }

      var confirmationResults = await
        files
          .Where(IsAttachableFile)
          .Choose(fr => fr.Ent)
          .Select<FileUpload, Func<Task<Result<string, ApiError>>>>(fu => () => emitter.Emit(() => new AnyState(
            new[]
              {
                new FileConfirmed(fu.Id),
                attachedFiles
                  .FirstOrNone(af => af.Id == fu.Id)
                  .Bind(af => af.Tags.Apply(Optional))
                  .Map<EventModelEvent>(t => new FileTagged(fu.Id, t))
              }
              .Choose()
              .ToArray())))
          .Parallel();

      return await confirmationResults
        .Aggregate(
          Ok<string[], ApiError>([]),
          (acc, result) => acc.Bind(ids => result.Map(id => ids.Append(id).ToArray()))
        )
        .ToTask()
        .Async()
        .Bind<FileUpload[]>(ids => ids
          .Select<string, Func<Task<FetchResult<FileUpload>>>>(fid =>
            () => fetcher.Fetch<FileUpload>(Some<StrongId>(new StrongGuid(Guid.Parse(fid))))
          )
          .Parallel()
          .Map(fus => fus.Choose(fu => fu.Ent).ToArray())
        );
    }

    bool IsAttachableFile(FetchResult<FileUpload> fr) => fr.Ent.Match(e => e.State != "deleted", () => false);

    async Task<Result<Shape, ApiError>> ValidateRequest(HttpRequest req) =>
      await Serialization
        .Deserialize<Shape>(req.Body)
        .Async()
        .Match(
          async tuple =>
          {
            var (body, shape) = tuple;
            if (shape is null)
            {
              return new ValidationError(["Could not deserialize the request body to the expected contract"]);
            }

            var nullabilityViolations = GetNullabilityViolations(shape);
            if (nullabilityViolations.Length != 0)
            {
              return new ValidationError(nullabilityViolations);
            }

            if (!UsesValidationRules)
            {
              return Validate(shape);
            }

            var rules =
              await fetcher
                .Fetch<FrameworkValidationRuleEntity>(new StrongString(routeSegment))
                .Map(re =>
                    re
                      .Ent.Map(e => e.JsonLogicRules)
                      .DefaultValue([JsonSerializer.Deserialize<Rule>("[]")!]) // Default, accept-everything rule.
                );

            var node = JsonNode.Parse(body);

            var validationResult = rules
              .Aggregate(
                Array.Empty<string>(),
                (e, r) => e.Concat(r.Apply(node).Deserialize<List<string>>()!).ToArray()
              )
              .ToArray()
              .Apply(e => new JsonLogicValidationResult(e))
              .ToResult()
              .Map(_ => shape);

            return validationResult.Bind(Validate);
          },
          err => Error<Shape, ApiError>(new ValidationError([err])).ToTask());

    Func<AsyncResult<EventInsertion, ApiError>> GetDecider(
      Shape command,
      Option<UserSecurity> user,
      Option<Guid> tenantId,
      HttpRequest request,
      FileUpload[] files
    ) =>
      () =>
        from entityId in TryGetEntityId(command, user, tenantId).ToTask().Async()
        from findResult in fetcher.SafeFetch<Entity>(entityId)
        from customAuth in ApplyCustomAuthorization(user, request, findResult.Ent, command).ToTask().Async()
        from decision in command
          .Decide(
            findResult.Ent,
            user.Match<UserSecurity?>(us => us, () => null),
            files,
            tenantId.Match<Guid?>(tid => tid, () => null)
          )
          .Map(d => d.WithRevision(findResult.Revision))
          .ToTask()
          .Async()
        select decision;

    Result<Option<StrongId>, ApiError> TryGetEntityId(
      Shape command,
      Option<UserSecurity> user,
      Option<Guid> tenantId
    ) =>
      command switch
      {
        TenantEventModelCommand<Entity> tc => user
          .Bind(u => tenantId.Map(id => (u, id)))
          .Bind(tuple => tc.TryGetEntityId(tuple.u, tuple.id))
          .Apply(Ok<Option<StrongId>, ApiError>),
        EventModelCommand<Entity> c => user.Bind(u => c.TryGetEntityId(u)).Apply(Ok<Option<StrongId>, ApiError>),
        _ => new DisasterError(
          "This command definition appears to have an unexpected inheritance of TenantEventModelCommand.")
      };

    Result<Unit, ApiError> ApplyCustomAuthorization(
      Option<UserSecurity> us,
      HttpRequest req,
      Option<Entity> entity,
      Shape command) =>
      Optional(CustomAuthorization)
        .Match<Result<Unit, ApiError>>(
          ca =>
          {
            var headers = req.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
            var query = req.Query.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
            var result = ca(us, headers, query, entity, command);
            return result ? unit : new ForbiddenError();
          },
          () => unit
        );

    async Task<Result<CommandAcceptedResult, ApiError>> Process(HttpContext context, Option<Guid> tenantId)
    {
      // ReSharper disable once ExplicitCallerInfoArgument
      using var activity = PrometheusMetrics.Source.StartActivity("CommandProcessing");
      activity?.SetTag("command.name", typeof(Shape).Name);
      if (!IsIdempotent())
      {
        return await Do();
      }

      var idempotencyKey = context.Request.Headers["IdempotencyKey"].ToString();
      if (invalidIdempotencyKeys.Any(k => k.Equals(idempotencyKey, StringComparison.InvariantCultureIgnoreCase)))
      {
        return new ValidationError(["Invalid IdempotencyKey"]);
      }

      var cache = await GetCache();
      return await cache.Match(OnLocked, OnSuccess, OnError, OnLockAvailable);

      Task<Result<CommandAcceptedResult, ApiError>> OnLocked(CacheLockedResult _) =>
        Task.FromResult<Result<CommandAcceptedResult, ApiError>>(
          new ConflictError("Another process is already handling this request")
        );

      Task<Result<CommandAcceptedResult, ApiError>> OnSuccess(SuccessCachedResult scr) =>
        Task.FromResult<Result<CommandAcceptedResult, ApiError>>(scr.Value);

      Task<Result<CommandAcceptedResult, ApiError>> OnError(ErrorCacheResult err) =>
        Task.FromResult<Result<CommandAcceptedResult, ApiError>>(Error(err.Value));

      async Task<Result<CommandAcceptedResult, ApiError>> OnLockAcquired(string _)
      {
        var result = await Do();
        await emitter.Emit(result.Match(SuccessDecider(idempotencyKey), ErrorDecider(idempotencyKey)));
        return result;
      }

      Func<CommandAcceptedResult, Func<AsyncResult<EventInsertion, ApiError>>> SuccessDecider(string key) =>
        car => () => fetcher
          .Fetch<IdempotencyCache>(new StrongString(key))
          .Map(fr => new ExistingStream(new IdempotentCommandAccepted(key, car)).WithRevision(fr.Revision))
          .Map(Ok<EventInsertion, ApiError>);

      Func<ApiError, Func<AsyncResult<EventInsertion, ApiError>>> ErrorDecider(string key) =>
        err => () => fetcher
          .Fetch<IdempotencyCache>(new StrongString(key))
          .Map(fr => new ExistingStream(new IdempotentCommandRejected(key, err)).WithRevision(fr.Revision))
          .Map(Ok<EventInsertion, ApiError>);

      Task<Result<CommandAcceptedResult, ApiError>> OnLockFailed(ApiError _) =>
        Task.FromResult<Result<CommandAcceptedResult, ApiError>>(
          new ConflictError("Another process is already handling this request")
        );

      async Task<Result<CommandAcceptedResult, ApiError>> OnLockAvailable(CacheLockAvailableResult cla)
      {
        var lockRequest = await emitter.Emit(
          () =>
            cla.Revision > -1
              ? new ExistingStream(new IdempotentCommandStarted(idempotencyKey, DateTime.UtcNow))
                .WithRevision(cla.Revision)
                .Apply(Ok<EventInsertion, ApiError>)
              : new CreateStream(new IdempotentCommandStarted(idempotencyKey, DateTime.UtcNow)),
          shouldSkipRetry: true);

        return await lockRequest.Match(OnLockAcquired, OnLockFailed);
      }

      async Task<Du4<CacheLockedResult, SuccessCachedResult, ErrorCacheResult, CacheLockAvailableResult>> GetCache()
      {
        var fetchResult = await fetcher.Fetch<IdempotencyCache>(new StrongString(idempotencyKey));
        return fetchResult.Ent
          .Match<Du4<CacheLockedResult, SuccessCachedResult, ErrorCacheResult, CacheLockAvailableResult>>(
            c => c.State switch
            {
              IdempotentRequestState.Accepted =>
                Second<CacheLockedResult, SuccessCachedResult, ErrorCacheResult, CacheLockAvailableResult>(
                  new SuccessCachedResult(c.StoredSuccess!)
                ),
              IdempotentRequestState.Rejected => new ErrorCacheResult(c.StoredError!),
              IdempotentRequestState.Pending => DateTime.UtcNow < c.LockedUntil
                ? new CacheLockedResult()
                : new CacheLockAvailableResult(fetchResult.Revision),
              IdempotentRequestState.New => new CacheLockAvailableResult(fetchResult.Revision),
              _ => throw new ArgumentOutOfRangeException()
            },
            () => new CacheLockAvailableResult(fetchResult.Revision)
          );
      }

      AsyncResult<CommandAcceptedResult, ApiError> Do()
      {
        return Go();

        async Task<Result<CommandAcceptedResult, ApiError>> Go()
        {
          var result = await (
            from command in context.Request.Apply(ValidateRequest).Async()
            from files in ValidateFiles(command).Async()
            from authorize in FrameworkSecurity.Authorization(context, fetcher, emitter, settings, Auth, tenantId)
            from entityId in emitter
              .Emit(
                GetDecider(command, authorize, tenantId, context.Request, files),
                new EventContext(Guid.NewGuid().ToString(), null, authorize.Match<string?>(u => u.Sub, () => null))
              )
              .Async()
            select new CommandAcceptedResult(entityId));

          result.Iter(
            // ReSharper disable once AccessToDisposedClosure
            _ => activity?.AddTag("command.result", "success"),
            // ReSharper disable once AccessToDisposedClosure
            err => activity?.AddTag(
              "command.result",
              err switch
              {
                DisasterError => "disaster",
                ValidationError => "validation-error",
                CorruptStreamError => "corrupt-stream",
                ConflictError => "conflict",
                NotFoundError => "not-found",
                ForbiddenError => "forbidden",
                UnauthorizedError => "unauthorized",
                _ => "unknown"
              })
          );

          return result;
        }
      }

      bool IsIdempotent() =>
        context.Request.Headers.ContainsKey("IdempotencyKey")
        && !string.IsNullOrWhiteSpace(context.Request.Headers["IdempotencyKey"]);
    }
  }

  private Result<Shape, ApiError> Validate(Shape command) =>
    command
      .Validate()
      .ToArray()
      .Apply<string[], Result<Shape, ApiError>>(err => err.Length == 0 ? command : new ValidationError(err));

  public override int GetHashCode() => routeSegment.GetHashCode();
}
