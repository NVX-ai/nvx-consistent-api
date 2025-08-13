using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nvx.ConsistentAPI.Framework.SignalRMessage;

namespace Nvx.ConsistentAPI;

public record AssignApplicationPermission(string Sub, string Permission) : EventModelCommand<UserSecurity>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Sub);

  public Result<EventInsertion, ApiError> Decide(
    Option<UserSecurity> us,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new AnyState(new ApplicationPermissionAssigned(Sub, Permission));
}

public record RevokeApplicationPermission(string Sub, string Permission) : EventModelCommand<UserSecurity>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Sub);

  public Result<EventInsertion, ApiError> Decide(
    Option<UserSecurity> us,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new AnyState(new ApplicationPermissionRevoked(Sub, Permission));
}

public record AssignTenantPermission(string Sub, string Permission) : TenantEventModelCommand<UserSecurity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(Sub);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<UserSecurity> entity,
    UserSecurity user,
    FileUpload[] files) => new AnyState(new TenantPermissionAssigned(Sub, Permission, tenantId));
}

public record RevokeTenantPermission(string Sub, string Permission) : TenantEventModelCommand<UserSecurity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(Sub);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<UserSecurity> entity,
    UserSecurity user,
    FileUpload[] files
  ) =>
    new AnyState(new TenantPermissionRevoked(Sub, Permission, tenantId));
}

public record AssignTenantRole(string UserSub, Guid RoleId) : TenantEventModelCommand<UserSecurity>
{
  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<UserSecurity> entity,
    UserSecurity user,
    FileUpload[] files) =>
    new AnyState(new TenantRoleAssigned(UserSub, RoleId, tenantId));

  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(UserSub);
}

public record RevokeTenantRole(string UserSub, Guid RoleId) : TenantEventModelCommand<UserSecurity>
{
  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<UserSecurity> entity,
    UserSecurity user,
    FileUpload[] files) =>
    new AnyState(new TenantRoleRevoked(UserSub, RoleId, tenantId));

  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(UserSub);
}

public record AddToTenant(string Sub) : TenantEventModelCommand<UserSecurity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(Sub);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<UserSecurity> entity,
    UserSecurity user,
    FileUpload[] files) => new AnyState(new AddedToTenant(Sub, tenantId));
}

public record RemoveFromTenant(string Sub) : TenantEventModelCommand<UserSecurity>
{
  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(Sub);

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<UserSecurity> entity,
    UserSecurity user,
    FileUpload[] files) => new AnyState(new RemovedFromTenant(Sub, tenantId));
}

public record AddedToTenant(string Sub, Guid TenantId) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record RemovedFromTenant(string Sub, Guid TenantId) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record ApplicationPermissionAssigned(string Sub, string Permission) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record ApplicationPermissionRevoked(string Sub, string Permission) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record TenantPermissionAssigned(string Sub, string Permission, Guid TenantId) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record TenantPermissionRevoked(string Sub, string Permission, Guid TenantId) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record UserNameReceived(string Sub, string FullName) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record UserEmailReceived(string Sub, string Email) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record TenantDetailsReceived(string Sub, Guid TenantId, string TenantName) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record TenantDetails(Guid TenantId, string TenantName);

public record TenantRoleAssigned(string Sub, Guid RoleId, Guid TenantId) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public record TenantRoleRevoked(string Sub, Guid RoleId, Guid TenantId) : EventModelEvent
{
  public string GetSwimLane() => UserSecurity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(Sub);
}

public partial record UserSecurity(
  string Sub,
  string Email,
  string FullName,
  Dictionary<Guid, UserSecurity.ReceivedRole[]> TenantRoles,
  string[] ApplicationPermissions,
  Dictionary<Guid, string[]> ExplicitTenantPermissions,
  TenantDetails[] Tenants)
  : EventModelEntity<UserSecurity>,
    Folds<ApplicationPermissionAssigned, UserSecurity>,
    Folds<ApplicationPermissionRevoked, UserSecurity>,
    Folds<TenantPermissionAssigned, UserSecurity>,
    Folds<TenantPermissionRevoked, UserSecurity>,
    Folds<AddedToTenant, UserSecurity>,
    Folds<RemovedFromTenant, UserSecurity>,
    Folds<UserNameReceived, UserSecurity>,
    Folds<UserEmailReceived, UserSecurity>,
    Folds<TenantDetailsReceived, UserSecurity>,
    Folds<TenantRoleAssigned, UserSecurity>,
    Folds<TenantRoleRevoked, UserSecurity>
{
  public const string StreamPrefix = "user-security-";

  public static readonly EventModel Get =
    new()
    {
      Entities =
      [
        new EntityDefinition<UserSecurity, StrongString>
        {
          Defaulter = Defaulted,
          StreamPrefix = StreamPrefix,
          CacheExpiration = TimeSpan.FromMinutes(5)
        }
      ],
      Commands =
      [
        new CommandDefinition<AssignApplicationPermission, UserSecurity>
        {
          Auth = new PermissionsRequireOne("permission-management"), AreaTag = OperationTags.Authorization
        },
        new CommandDefinition<RevokeApplicationPermission, UserSecurity>
        {
          Auth = new PermissionsRequireOne("permission-management"), AreaTag = OperationTags.Authorization
        },
        new CommandDefinition<AssignTenantPermission, UserSecurity>
        {
          Auth = new PermissionsRequireOne("permission-management"), AreaTag = OperationTags.Authorization
        },
        new CommandDefinition<RevokeTenantPermission, UserSecurity>
        {
          Auth = new PermissionsRequireOne("permission-management"), AreaTag = OperationTags.Authorization
        },
        new CommandDefinition<AssignTenantRole, UserSecurity>
        {
          Auth = new PermissionsRequireOne("permission-management"), AreaTag = OperationTags.Authorization
        },
        new CommandDefinition<RevokeTenantRole, UserSecurity>
        {
          Auth = new PermissionsRequireOne("permission-management"), AreaTag = OperationTags.Authorization
        },
        new CommandDefinition<AddToTenant, UserSecurity>
        {
          Auth = new PermissionsRequireOne("tenancy-management"), AreaTag = OperationTags.Authorization
        },
        new CommandDefinition<RemoveFromTenant, UserSecurity>
        {
          Auth = new PermissionsRequireOne("tenancy-management"), AreaTag = OperationTags.Authorization
        }
      ],
      ReadModels =
      [
        new ReadModelDefinition<UserSecurityReadModel, UserSecurity>
        {
          StreamPrefix = StreamPrefix,
          Projector = us =>
          [
            new UserSecurityReadModel(
              us.Sub,
              us.Sub,
              us.Email,
              us.FullName,
              us.TenantRoles,
              us.ApplicationPermissions,
              us.TenantPermissions,
              us.Tenants
            )
          ],
          Auth = new PermissionsRequireOne("permission-management"),
          AreaTag = OperationTags.Authorization
        }
      ],
      Projections =
      [
        new AssignToTenantOnTenantPermissionAssigned(),
        new SendNotificationOnTenantPermissionAssigned(),
        new SendNotificationOnTenantPermissionRevoked()
      ],
      InterestTriggers =
      [
        new InitiatesInterest<TenantRoleAssigned>(evt =>
        [
          new EntityInterestManifest(
            (evt as EventModelEvent).GetStreamName(),
            evt.GetEntityId(),
            RoleEntity.GetStreamName(new RoleId(evt.RoleId, evt.TenantId)),
            new RoleId(evt.RoleId, evt.TenantId))
        ]),
        new StopsInterest<TenantRoleRevoked>(evt =>
        [
          new EntityInterestManifest(
            (evt as EventModelEvent).GetStreamName(),
            evt.GetEntityId(),
            RoleEntity.GetStreamName(new RoleId(evt.RoleId, evt.TenantId)),
            new RoleId(evt.RoleId, evt.TenantId))
        ])
      ]
    };

  public Guid[] ActiveInTenants =>
    Tenants
      .Select(t => t.TenantId)
      .Concat(TenantPermissions.Keys)
      .Distinct()
      .ToArray();

  public Dictionary<Guid, string[]> TenantPermissions =>
    ExplicitTenantPermissions
      .Concat(
        TenantRoles
          .SelectMany(kvp => kvp.Value.Select(r => new KeyValuePair<Guid, string[]>(kvp.Key, r.Permissions))))
      .GroupBy(kvp => kvp.Key)
      .ToDictionary(kvp => kvp.Key, kvp => kvp.SelectMany(p => p.Value).Distinct().ToArray());

  public string GetStreamName() => GetStreamName(Sub);

  public async ValueTask<UserSecurity> Fold(AddedToTenant att, EventMetadata metadata, RevisionFetcher fetcher) =>
    this with
    {
      ExplicitTenantPermissions = AddTo(att.TenantId),
      Tenants = Tenants
        .Where(t => t.TenantId != att.TenantId)
        .Append(
          new TenantDetails(
            att.TenantId,
            await fetcher.LatestFetch<Tenant>(new StrongGuid(att.TenantId)).Map(t => t.Name).DefaultValue("")))
        .ToArray()
    };

  public ValueTask<UserSecurity> Fold(
    ApplicationPermissionAssigned ara,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        ApplicationPermissions =
        ApplicationPermissions.Contains(ara.Permission)
          ? ApplicationPermissions
          : ApplicationPermissions.Append(ara.Permission).ToArray()
      });

  public ValueTask<UserSecurity> Fold(
    ApplicationPermissionRevoked arr,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with { ApplicationPermissions = ApplicationPermissions.Except([arr.Permission]).ToArray() });

  public ValueTask<UserSecurity> Fold(
    RemovedFromTenant rft,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        ExplicitTenantPermissions = RemoveFrom(rft.TenantId),
        Tenants = Tenants.Where(t => t.TenantId != rft.TenantId).ToArray()
      });

  public ValueTask<UserSecurity> Fold(
    TenantDetailsReceived evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        Tenants = Tenants
          .Where(t => t.TenantId != evt.TenantId)
          .Append(new TenantDetails(evt.TenantId, evt.TenantName))
          .ToArray()
      });

  public ValueTask<UserSecurity> Fold(
    TenantPermissionAssigned tra,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        ExplicitTenantPermissions =
        ModifyTenantPermissions(
          tra.TenantId,
          permissions => permissions.Contains(tra.Permission)
            ? permissions
            : permissions.Append(tra.Permission).ToArray())
      });

  public ValueTask<UserSecurity> Fold(
    TenantPermissionRevoked trr,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        ExplicitTenantPermissions = ModifyTenantPermissions(
          trr.TenantId,
          permissions => permissions.Except([trr.Permission]).ToArray())
      });

  public async ValueTask<UserSecurity> Fold(
    TenantRoleAssigned tra,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    await fetcher
      .LatestFetch<RoleEntity>(new RoleId(tra.RoleId, tra.TenantId))
      .Match(
        r => this with
        {
          TenantRoles = ModifyTenantRoles(
            tra.TenantId,
            roles => roles
              .Where(tr => tr.Id != r.Id)
              .Append(new ReceivedRole(r.Id, r.Name, r.Permissions))
              .ToArray())
        },
        () => this);

  public ValueTask<UserSecurity> Fold(
    TenantRoleRevoked trr,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(
      this with
      {
        TenantRoles = ModifyTenantRoles(
          trr.TenantId,
          roles => roles.Where(tr => tr.Id != trr.RoleId).ToArray())
      });

  public ValueTask<UserSecurity> Fold(
    UserEmailReceived uer,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Email = uer.Email });

  public ValueTask<UserSecurity> Fold(
    UserNameReceived unr,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { FullName = unr.FullName });

  private Dictionary<Guid, string[]> ModifyTenantPermissions(Guid tenantId, Func<string[], string[]> modify)
  {
    var newPermissions = new Dictionary<Guid, string[]>(ExplicitTenantPermissions);
    newPermissions[tenantId] = modify(
      newPermissions.TryGetValue(tenantId, out var currentTenantPermissions)
        ? currentTenantPermissions
        : []
    );
    return new Dictionary<Guid, string[]>(newPermissions);
  }

  private Dictionary<Guid, ReceivedRole[]> ModifyTenantRoles(Guid tenantId, Func<ReceivedRole[], ReceivedRole[]> modify)
  {
    var newRoles = new Dictionary<Guid, ReceivedRole[]>(TenantRoles);
    newRoles[tenantId] = modify(
      newRoles.TryGetValue(tenantId, out var currentTenantRoles)
        ? currentTenantRoles
        : []
    );
    return new Dictionary<Guid, ReceivedRole[]>(newRoles);
  }

  private Dictionary<Guid, string[]> RemoveFrom(Guid tenantId)
  {
    var newPermissions = new Dictionary<Guid, string[]>(ExplicitTenantPermissions);
    newPermissions.Remove(tenantId);
    return new Dictionary<Guid, string[]>(newPermissions);
  }

  private Dictionary<Guid, string[]> AddTo(Guid tenantId)
  {
    var newPermissions = new Dictionary<Guid, string[]>(ExplicitTenantPermissions);
    newPermissions.TryAdd(tenantId, []);
    return new Dictionary<Guid, string[]>(newPermissions);
  }

  public static string GetStreamName(string sub) => $"{StreamPrefix}{sub}";

  public static UserSecurity Defaulted(StrongString sub) =>
    new(
      sub.Value,
      string.Empty,
      string.Empty,
      new Dictionary<Guid, ReceivedRole[]>(),
      [],
      new Dictionary<Guid, string[]>(),
      []);

  public record ReceivedRole(Guid Id, string Name, string[] Permissions);
}

public class AssignToTenantOnTenantPermissionAssigned :
  ProjectionDefinition<TenantPermissionAssigned, AddedToTenant, UserSecurity, UserSecurity, StrongString>
{
  public override string Name => "projection-tenant-permission-assigned-to-added-to-tenant";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<AddedToTenant> Project(
    TenantPermissionAssigned eventToProject,
    UserSecurity e,
    Option<UserSecurity> projectionEntity,
    StrongString projectionId,
    Guid sourceEventUuid,
    EventMetadata metadata) =>
    e.Tenants.Any(t => t.TenantId == eventToProject.TenantId)
      ? None
      : new AddedToTenant(e.Sub, eventToProject.TenantId);

  public override IEnumerable<StrongString> GetProjectionIds(
    TenantPermissionAssigned sourceEvent,
    UserSecurity sourceEntity,
    Guid sourceEventId) =>
    [new(Guid.NewGuid().ToString())];
}

public class SendNotificationOnTenantPermissionAssigned :
  ProjectionDefinition<TenantPermissionAssigned, SignalRMessageScheduled, UserSecurity, SignalRMessageEntity,
    SignalRMessageId>
{
  public override string Name => "projection-tenant-permission-assigned-to-send-notification";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<SignalRMessageScheduled> Project(
    TenantPermissionAssigned eventToProject,
    UserSecurity e,
    Option<SignalRMessageEntity> projectionEntity,
    SignalRMessageId projectionId,
    Guid sourceEventUuid,
    EventMetadata metadata) =>
    new SignalRMessageScheduled(
      projectionId.Value,
      [eventToProject.Sub],
      $"Tenant permission {eventToProject.Permission} assigned",
      "tenant-permission-assigned",
      e.Sub,
      e.GetType().Name,
      e.Sub,
      null,
      "permission",
      DateTime.UtcNow);

  public override IEnumerable<SignalRMessageId> GetProjectionIds(
    TenantPermissionAssigned sourceEvent,
    UserSecurity sourceEntity,
    Guid sourceEventId) =>
    [new(Guid.NewGuid())];
}

public class SendNotificationOnTenantPermissionRevoked :
  ProjectionDefinition<TenantPermissionRevoked, SignalRMessageScheduled, UserSecurity, SignalRMessageEntity,
    SignalRMessageId>
{
  public override string Name => "projection-tenant-permission-revoked-to-send-notification";
  public override string SourcePrefix => UserSecurity.StreamPrefix;

  public override Option<SignalRMessageScheduled> Project(
    TenantPermissionRevoked eventToProject,
    UserSecurity e,
    Option<SignalRMessageEntity> projectionEntity,
    SignalRMessageId projectionId,
    Guid sourceEventUuid,
    EventMetadata metadata) =>
    new SignalRMessageScheduled(
      projectionId.Value,
      [eventToProject.Sub],
      $"Tenant Permission {eventToProject.Permission} revoked",
      "tenant-permission-revoked",
      e.Sub,
      e.GetType().Name,
      e.Sub,
      null,
      "permission",
      DateTime.UtcNow);

  public override IEnumerable<SignalRMessageId> GetProjectionIds(
    TenantPermissionRevoked sourceEvent,
    UserSecurity sourceEntity,
    Guid sourceEventId) =>
    [new(Guid.NewGuid())];
}

public record UserSecurityReadModel(
  string Id,
  string Sub,
  string Email,
  string FullName,
  Dictionary<Guid, UserSecurity.ReceivedRole[]> TenantRoles,
  string[] ApplicationPermissions,
  Dictionary<Guid, string[]> TenantPermissions,
  TenantDetails[] Tenants) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongString(Id);
}

public static class UserSecurityDefinitions
{
  public static void InitializeEndpoints(
    WebApplication app,
    Emitter emitter,
    Fetcher fetcher,
    GeneratorSettings settings)
  {
    if (!settings.EnabledFeatures.HasEndpoints())
    {
      return;
    }

    Delegate handler = async (HttpContext context) =>
      await GetPermissions(context, None).Apply(Respond<string[]>(context));

    app
      .MapGet("/current-user/permissions", handler)
      .Produces<string[]>()
      .Produces<ErrorResponse>(500)
      .WithTags(OperationTags.CurrentUser)
      .ApplyAuth(new EveryoneAuthenticated());

    Delegate tenantHandler = async (HttpContext context, Guid tenantId) =>
      await GetPermissions(context, tenantId).Apply(Respond<string[]>(context));

    app
      .MapGet("/tenant/{tenantId:Guid}/current-user/permissions", tenantHandler)
      .Produces<string[]>()
      .Produces<ErrorResponse>(500)
      .WithTags(OperationTags.CurrentUser)
      .ApplyAuth(new EveryoneAuthenticated());

    Delegate definitionHandler = async (HttpContext context) =>
      await GetUser(context).Apply(Respond<UserSecurity>(context));

    app
      .MapGet("/current-user", definitionHandler)
      .Produces<UserSecurity>()
      .Produces<ErrorResponse>(500)
      .WithTags(OperationTags.CurrentUser)
      .ApplyAuth(new EveryoneAuthenticated());

    return;

    AsyncResult<string[], ApiError> GetPermissions(HttpContext context, Option<Guid> tenantId) =>
      FrameworkSecurity
        .Authorization(context, fetcher, emitter, settings, new EveryoneAuthenticated(), tenantId)
        .Bind(opt =>
          opt.Result<ApiError>(new UnauthorizedError()).Map(FrameworkSecurity.EffectivePermissions(tenantId)));

    AsyncResult<UserSecurity, ApiError> GetUser(HttpContext context) =>
      FrameworkSecurity
        .Authorization(context, fetcher, emitter, settings, new EveryoneAuthenticated(), None)
        .Bind(opt => opt.Result<ApiError>(new UnauthorizedError()));

    static Func<AsyncResult<T, ApiError>, Task> Respond<T>(HttpContext context) =>
      async r =>
      {
        await r.Iter(
          async value =>
          {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(value);
          },
          async error => await error.Respond(context));
      };
  }
}
