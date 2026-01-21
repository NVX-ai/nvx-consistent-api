namespace Nvx.ConsistentAPI;

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
            evt.GetStreamName(),
            evt.GetEntityId(),
            RoleEntity.GetStreamName(new RoleId(evt.RoleId, evt.TenantId)),
            new RoleId(evt.RoleId, evt.TenantId))
        ]),
        new StopsInterest<TenantRoleRevoked>(evt =>
        [
          new EntityInterestManifest(
            evt.GetStreamName(),
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
