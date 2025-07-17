# Tenancy
Some applications have to compartimentalize data, so that data created by one organization can only be requested by users belonging to that organization. Consistent API supports this out of the box. [Commands](./command.md) and [Read Models](./read-model.md) can me made `TenantBound`, so their urls start with `/tenant/{tenantId}/`, and only users assigned to a tenant (receiving a role in a tenant assigns the user automatically to the tenant) can access them. There are also tenant based roles, so users can be assigned different levels of access in each tenant.

## Read models
Any read model implementing `IsTenantBound` will be automatically filtering its queries based on the tenant id on the url, and it will not be possible to query them without a tenant id.

## Commands
Commands are a bit trickier, as the `Decide` method has a different signature, that includes the tenantID, but nothing special beyond that:
```cs
public record RegisterOrganizationBuilding(string Name) : TenantEventModelCommand<OrganizationBuilding>
{
  public Option<string> TryGetEntityId() => Name;
  public Result<EventInsertion, ApiError> Decide(Guid tenantId, Option<OrganizationBuilding> entity, UserSecurity user) =>
    entity.Match<Result<EventInsertion, ApiError>>(
      _ => new ConflictError("Tried to create an organization building that already existed"),
      () => new CreateStream(new OrganizationBuildingRegistered(Name, tenantId))
    );
}
```
This command registers a building for a tenant.
