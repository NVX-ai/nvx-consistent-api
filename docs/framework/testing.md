# Automated checks
Consistent API comes with a tool to start a test instance of the framework, execute commands, query read models, and even inject dependencies for your tests.

In order to start the instance, reference the package `ConsistentAPI.TestUtils`, and request a `TestSetup` from it.:
```cs
await using var setup = await TestSetup.Initialize(EventModel.GetModel());
```

## The test API
These are all the methods exposed by the test setup:
### Upload a file
```cs
Task<CommandAcceptedResult> Upload()
```
Uploads a file called `text.txt` with the content `banana`, and returns the ID of the uploaded file.

### Commands
```cs
Task<CommandAcceptedResult> Command<C>(
  C command,
  bool asAdmin = false, 
  Guid? tenantId = null, 
  Dictionary<string, string>? headers = null)
```
Issues a command and returns the ID of the affected entity.

#### Parameters
- `command`: The command to issue.
- `asAdmin`: Whether to issue the command as an admin.
- `tenantId`: The tenant ID to use, only for tenant bound commands.
- `headers`: The headers to send with the request.

```cs
Task<ErrorResponse> FailingCommand<C>(
  C command,
  int responseCode,
  Guid? tenantId = null,
  bool asAdmin = false)
```
Issues a command, expecting it to fail with the given response code.

#### Parameters
- `command`: The command to issue.
- `responseCode`: The response code to expect.
- `tenantId`: The tenant ID to use, only for tenant bound commands.
- `asAdmin`: Whether to issue the command as an admin.

```cs
Task<UserSecurity> CurrentUser(bool asAdmin = false)
```
Returns the current user.

#### Parameters
- `asAdmin`: Whether to return the admin user.

### Read models
```cs
Task<PageResult<Rm>> ReadModels<Rm>(
  bool asAdmin = false,
  Guid? tenantId = null,
  Dictionary<string, string[]>? queryParameters = null) where Rm : EventModelReadModel
```
Requests a page of read models.

#### Parameters
- `asAdmin`: Whether request the page as an admin.
- `tenantId`: The tenant ID to use, only for tenant bound read models.
- `queryParameters`: The query parameters to send with the request.

```cs
Task<Rm> ReadModel<Rm>(
  string id,
  Dictionary<string, string[]>? queryParameters = null,
  bool asAdmin = false) where Rm : EventModelReadModel
```
Requests a single model by ID.

#### Parameters
- `id`: The ID of the read model.
- `queryParameters`: The query parameters to send with the request.
- `asAdmin`: Whether request the read model as an admin.

```cs
Task ReadModelNotFound<Rm>(string id, bool asAdmin = false)
```
Checks that a read model with the given ID is not found.

#### Parameters
- `id`: The ID of the read model.
- `asAdmin`: Whether request the read model as an admin.

```cs
Task ForbiddenReadModel<Rm>(Guid? tenantId = null)
```
Checks that the current user is forbidden to access the read model.

#### Parameters
- `tenantId`: The tenant ID to use, only for tenant bound read models.