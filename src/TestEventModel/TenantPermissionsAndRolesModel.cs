using Nvx.ConsistentAPI;

namespace TestEventModel;

public static class TenantPermissionsAndRolesModel
{
  public static readonly EventModel Model = new()
  {
    Entities = [PermissionsAndRolesEntity.Definition],
    Commands = [ActUponPermissionsAndRolesEntity.Definition]
  };
}

public record PermissionsAndRolesEntityId(Guid Id) : StrongId
{
  public override string StreamId() => ToString();
  public override string ToString() => Id.ToString();
}

public partial record PermissionsAndRolesEntity(Guid Id, Guid TenantId) : EventModelEntity<PermissionsAndRolesEntity>,
  Folds<PermissionsAndRolesEvent, PermissionsAndRolesEntity>
{
  public const string StreamPrefix = "permissions-and-roles-entity-";

  public static readonly EntityDefinition Definition =
    new EntityDefinition<PermissionsAndRolesEntity, PermissionsAndRolesEntityId>
    {
      Defaulter = Defaulted,
      StreamPrefix = StreamPrefix
    };

  public readonly PermissionsAndRolesEntityId EntityId = new(Id);
  public string GetStreamName() => GetStreamName(EntityId);

  public ValueTask<PermissionsAndRolesEntity> Fold(
    PermissionsAndRolesEvent evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) => ValueTask.FromResult(this);

  public static PermissionsAndRolesEntity Defaulted(PermissionsAndRolesEntityId id) => new(id.Id, Guid.NewGuid());

  public static string GetStreamName(PermissionsAndRolesEntityId entityId) => $"{StreamPrefix}{entityId}";
}

public record PermissionsAndRolesEvent(Guid Id, Guid TenantId) : EventModelEvent
{
  public string SwimLane => PermissionsAndRolesEntity.StreamPrefix;
  public StrongId GetEntityId() => new PermissionsAndRolesEntityId(Id);
}

public record ActUponPermissionsAndRolesEntity(Guid Id) : TenantEventModelCommand<PermissionsAndRolesEntity>
{
  public const string Permission = "the-permission-for-roles-command";

  public static readonly EventModelingCommandArtifact Definition =
    new CommandDefinition<ActUponPermissionsAndRolesEntity, PermissionsAndRolesEntity>
    {
      AreaTag = "PermissionsAndRoles",
      Auth = new PermissionsRequireAll(Permission)
    };

  public Result<EventInsertion, ApiError> Decide(
    Guid tenantId,
    Option<PermissionsAndRolesEntity> entity,
    UserSecurity user,
    FileUpload[] files) => new AnyState(new PermissionsAndRolesEvent(Id, tenantId));

  public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new PermissionsAndRolesEntityId(Id);
}
