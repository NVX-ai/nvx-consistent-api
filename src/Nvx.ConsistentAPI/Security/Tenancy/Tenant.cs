namespace Nvx.ConsistentAPI;

public record CreateTenant(Guid Id, string Name) : EventModelCommand<Tenant>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(Id);

  public Result<EventInsertion, ApiError> Decide(
    Option<Tenant> tenant,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    tenant.Match<Result<EventInsertion, ApiError>>(
      _ => new ConflictError("Tried to create an already existing tenant."),
      () => new CreateStream(new TenantCreated(Id, Name))
    );

  public IEnumerable<string> Validate() => [];
}

public record RenameTenant(Guid Id, string NewName) : EventModelCommand<Tenant>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(Id);

  public Result<EventInsertion, ApiError> Decide(
    Option<Tenant> tenant,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new ExistingStream(new TenantRenamed(Id, NewName));

  public IEnumerable<string> Validate() => [];
}

public record EnableTenant(Guid Id) : EventModelCommand<Tenant>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(Id);

  public Result<EventInsertion, ApiError> Decide(
    Option<Tenant> tenant,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new ExistingStream(new TenantEnabled(Id));

  public IEnumerable<string> Validate() => [];
}

public record DisableTenant(Guid Id) : EventModelCommand<Tenant>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongGuid(Id);

  public Result<EventInsertion, ApiError> Decide(
    Option<Tenant> tenant,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new ExistingStream(new TenantDisabled(Id));

  public IEnumerable<string> Validate() => [];
}

public record TenantCreated(Guid Id, string Name) : EventModelEvent
{
  public string GetStreamName() => Tenant.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record TenantRenamed(Guid Id, string NewName) : EventModelEvent
{
  public string GetStreamName() => Tenant.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record TenantEnabled(Guid Id) : EventModelEvent
{
  public string GetStreamName() => Tenant.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record TenantDisabled(Guid Id) : EventModelEvent
{
  public string GetStreamName() => Tenant.GetStreamName(Id);
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public partial record Tenant(Guid Id, string Name, bool Enabled) :
  EventModelEntity<Tenant>,
  Folds<TenantCreated, Tenant>,
  Folds<TenantRenamed, Tenant>,
  Folds<TenantEnabled, Tenant>,
  Folds<TenantDisabled, Tenant>
{
  public const string StreamPrefix = "framework-tenant-";

  public static readonly EventModel Get =
    new()
    {
      Commands =
      [
        new CommandDefinition<CreateTenant, Tenant>
        {
          Auth = new PermissionsRequireOne("tenancy-management"), AreaTag = OperationTags.TenancyManagement
        },
        new CommandDefinition<RenameTenant, Tenant>
        {
          Auth = new PermissionsRequireOne("tenancy-management"), AreaTag = OperationTags.TenancyManagement
        },
        new CommandDefinition<EnableTenant, Tenant>
        {
          Auth = new PermissionsRequireOne("tenancy-management"), AreaTag = OperationTags.TenancyManagement
        },
        new CommandDefinition<DisableTenant, Tenant>
        {
          Auth = new PermissionsRequireOne("tenancy-management"), AreaTag = OperationTags.TenancyManagement
        }
      ],
      ReadModels =
      [
        new ReadModelDefinition<TenantReadModel, Tenant>
        {
          StreamPrefix = StreamPrefix,
          Projector = entity => [new TenantReadModel(entity.Id.ToString(), entity.Name, entity.Enabled)],
          Auth = new PermissionsRequireOne("tenancy-management", "tenancy-read"),
          AreaTag = OperationTags.TenancyManagement
        }
      ],
      Entities = [new EntityDefinition<Tenant, StrongGuid> { Defaulter = Defaulted, StreamPrefix = StreamPrefix }]
    };

  public string GetStreamName() => GetStreamName(Id);

  public ValueTask<Tenant> Fold(TenantCreated tc, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(new Tenant(tc.Id, tc.Name, true));

  public ValueTask<Tenant> Fold(TenantDisabled td, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Enabled = false });

  public ValueTask<Tenant> Fold(TenantEnabled te, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Enabled = true });

  public ValueTask<Tenant> Fold(TenantRenamed tr, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Name = tr.NewName });

  public static string GetStreamName(Guid id) => $"{StreamPrefix}-{id}";

  public static Tenant Defaulted(StrongGuid id) => new(id.Value, string.Empty, false);
}

public record TenantReadModel(string Id, string Name, bool Enabled) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongGuid(Guid.Parse(Id));
}

public interface IsTenantBound
{
  Guid TenantId { get; }
}
