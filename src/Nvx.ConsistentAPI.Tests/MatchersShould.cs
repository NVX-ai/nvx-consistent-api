namespace Nvx.ConsistentAPI.Tests;

public class MatchersShould
{
  [Fact(DisplayName = "Map to conflict for command that expects no entity")]
  public void Map_to_conflict_for_command_that_expects_no_entity()
  {
    var command = new TestCreationCommand("1");
    var result = command.Decide(new TestEntity("1"), None, []);
    result.ShouldBeError(new ConflictError("Tried to create TestEntity when it already existed."));
  }

  [Fact(DisplayName = "Map to conflict for auth command that expects no entity when authorized")]
  public void Map_to_conflict_for_auth_command_that_expects_no_entity()
  {
    var command = new TestAuthCreationCommand("1");
    var result = command.Decide(new TestEntity("1"), TestData.UserWithNoPermissions(), []);
    result.ShouldBeError(new ConflictError("Tried to create TestEntity when it already existed."));
  }

  [Fact(DisplayName = "Map to unauthorized for auth command that expects no entity when unauthorized")]
  public void Map_to_unauthorized_for_auth_command_that_expects_no_entity_when_unauthorized()
  {
    var command = new TestAuthCreationCommand("1");
    var result = command.Decide(new TestEntity("1"), None, []);
    result.ShouldBeError(new UnauthorizedError());
  }

  [Fact(DisplayName = "Creates for auth command that expects no entity when unauthorized")]
  public void Creates_for_auth_command_that_expects_no_entity_when_unauthorized()
  {
    var command = new TestAuthCreationCommand("1");
    var result = command.Decide(None, TestData.UserWithNoPermissions(), []);
    result.ShouldBeOk(new CreateStream());
  }

  [Fact(DisplayName = "Map to create insertion for command that expects no entity")]
  public void Map_to_create_insertion_for_command_that_expects_no_entity()
  {
    var command = new TestCreationCommand("1");
    var result = command.Decide(None, None, []);
    result.ShouldBeOk(new CreateStream());
  }

  [Fact(DisplayName = "Map to already existing for tenant command that expects no entity")]
  public void Map_to_already_existing_for_tenant_command_that_expects_no_entity()
  {
    var command = new TestTenantCreationCommand("1");
    var result = command.Decide(Guid.NewGuid(), new TestEntity("1"), null!, []);
    result.ShouldBeError(new ConflictError("Tried to create TestEntity when it already existed."));
  }

  [Fact(DisplayName = "Map to create insertion for tenant command that expects no entity")]
  public void Map_to_create_insertion_for_tenant_command_that_expects_no_entity()
  {
    var command = new TestTenantCreationCommand("1");
    var result = command.Decide(Guid.NewGuid(), None, null!, []);
    result.ShouldBeOk(new CreateStream());
  }

  [Fact(DisplayName = "Required tenant command requires an entity")]
  public void Required_tenant_command_requires_an_entity()
  {
    var command = new TestRequiredTenantCommand("1");
    var result = command.Decide(Guid.NewGuid(), None, null!, []);
    result.ShouldBeError(new NotFoundError("TestEntity", "1"));
  }

  [Fact(DisplayName = "Required tenant command processes as expected for an existing entity")]
  public void Required_tenant_processes_as_expected_for_an_existing_entity()
  {
    var command = new TestRequiredTenantCommand("a");
    var result = command.Decide(Guid.NewGuid(), new TestEntity("a"), null!, []);
    result.ShouldBeOk(new ExistingStream());
  }

  [Fact(DisplayName = "Required command processes as expected for an existing entity")]
  public void Required_processes_as_expected_for_an_existing_entity()
  {
    var command = new TestRequiredCommand("a");
    var result = command.Decide(new TestEntity("a"), null!, []);
    result.ShouldBeOk(new ExistingStream());
  }

  [Fact(DisplayName = "Required command requires an entity")]
  public void Required_command_requires_an_entity()
  {
    var command = new TestRequiredCommand("a");
    var result = command.Decide(None, null!, []);
    result.ShouldBeError(new NotFoundError(nameof(TestEntity), "a"));
  }

  [Fact(DisplayName = "Required auth command processes as expected for an existing entity when authorized")]
  public void Required_auth_command_processes_as_expected_for_an_existing_entity_when_authorized()
  {
    var command = new TestAuthRequiredCommand("a");
    var result = command.Decide(new TestEntity("a"), TestData.UserWithNoPermissions(), []);
    result.ShouldBeOk(new ExistingStream());
  }

  [Fact(DisplayName = "Required auth command requires an entity when authorized")]
  public void Required_auth_command_requires_an_entity_when_authorized()
  {
    var command = new TestAuthRequiredCommand("a");
    var result = command.Decide(None, TestData.UserWithNoPermissions(), []);
    result.ShouldBeError(new NotFoundError(nameof(TestEntity), "a"));
  }

  [Fact(DisplayName = "Required auth command requires authorization")]
  public void Required_auth_command_requires_authorization()
  {
    var command = new TestAuthRequiredCommand("a");
    var result = command.Decide(None, None, []);
    result.ShouldBeError(new UnauthorizedError());
  }

  [Fact(DisplayName = "Required user command requires authorization")]
  public void Required_user_command_requires_authorization()
  {
    var command = new TestRequiredUserCommand("a");
    var result = command.Decide(None, None, []);
    result.ShouldBeError(new UnauthorizedError());
  }

  // ReSharper disable once ClassNeverInstantiated.Local
  // ReSharper disable once NotAccessedPositionalProperty.Local


  private record TestRequiredTenantCommand(string Id) : TenantEventModelCommand<TestEntity>
  {
    public Result<EventInsertion, ApiError> Decide(
      Guid tenantId,
      Option<TestEntity> entity,
      UserSecurity user,
      FileUpload[] files
    ) =>
      this.Require(entity, user, tenantId, _ => new ExistingStream());

    public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(Id);
    public IEnumerable<string> Validate() => [];
  }

  private record TestRequiredCommand(string Id) : EventModelCommand<TestEntity>
  {
    public Result<EventInsertion, ApiError> Decide(
      Option<TestEntity> entity,
      Option<UserSecurity> user,
      FileUpload[] files
    ) => this.Require(entity, _ => new ExistingStream());

    public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
    public IEnumerable<string> Validate() => [];
  }

  private record TestRequiredUserCommand(string Id) : EventModelCommand<TestEntity>
  {
    public Result<EventInsertion, ApiError> Decide(
      Option<TestEntity> entity,
      Option<UserSecurity> user,
      FileUpload[] files
    ) => this.Require(user, _ => new ExistingStream());

    public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
    public IEnumerable<string> Validate() => [];
  }

  private record TestAuthRequiredCommand(string Id) : EventModelCommand<TestEntity>
  {
    public Result<EventInsertion, ApiError> Decide(
      Option<TestEntity> entity,
      Option<UserSecurity> user,
      FileUpload[] files
    ) => this.Require(entity, user, (_, _) => new ExistingStream());

    public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
    public IEnumerable<string> Validate() => [];
  }

  private record TestCreationCommand(string Id) : EventModelCommand<TestEntity>
  {
    public Result<EventInsertion, ApiError> Decide(
      Option<TestEntity> entity,
      Option<UserSecurity> user,
      FileUpload[] files
    ) => this.ShouldCreate(entity, () => Array.Empty<EventModelEvent>());

    public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
    public IEnumerable<string> Validate() => [];
  }

  private record TestAuthCreationCommand(string Id) : EventModelCommand<TestEntity>
  {
    public Result<EventInsertion, ApiError> Decide(
      Option<TestEntity> entity,
      Option<UserSecurity> user,
      FileUpload[] files
    ) => this.ShouldCreate(entity, user, _ => Array.Empty<EventModelEvent>());

    public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(Id);
    public IEnumerable<string> Validate() => [];
  }

  private record TestTenantCreationCommand(string Id) : TenantEventModelCommand<TestEntity>
  {
    public Result<EventInsertion, ApiError> Decide(
      Guid tenantId,
      Option<TestEntity> entity,
      UserSecurity user,
      FileUpload[] files
    ) =>
      this.ShouldCreate(entity, () => Array.Empty<EventModelEvent>());

    public Option<StrongId> TryGetEntityId(UserSecurity user, Guid tenantId) => new StrongString(Id);
    public IEnumerable<string> Validate() => [];
  }
}

public partial record TestEntity(string Id) : EventModelEntity<TestEntity>
{
  public const string StreamPrefix = "test-entity-";
  public string GetStreamName() => throw new NotImplementedException();

  public static TestEntity Defaulted(StrongGuid id) => throw new NotImplementedException();
}
