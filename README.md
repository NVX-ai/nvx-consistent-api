# Getting Started

## Dependencies

1. [.NET Core 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
2. [Docker](https://www.docker.com/products/docker-desktop/)
3. Recommended (but not required) IDEs: [VS Code](https://code.visualstudio.com/)
   or [Rider](https://www.jetbrains.com/rider/)
4. [Postman](https://www.postman.com/downloads/) (or equivalent) for API Testing

## Building Infrastructure

The Consistent API infrastructure source is in src\ConsistentAPI but you can build the whole solution with
`dotnet build` a terminal in the root directory.

## Testing Infrastructure

The Consistent API infrastructure integration test source is in src\ConsistentAPI.Tests but you can run them by doing
the following from a terminal in the root directory:

```
docker compose -f test-docker-compose-x64.yaml up -d
dotnet test src/ConsistentAPI.Tests/ConsistentAPI.Tests.csproj
```

See integration-tests.yml for more details about steps for running automated tests as part of a devops pipeline.

## Using Test API on top of Infrastructure

The Consistent API TestApi source is in src\TestApi but to get the TestApi up and running so you can use it do the
following:

1. Run `docker compose up` from a terminal in the root directory.

2. Open Docker and confirm that you can see eventstore db, postgres, and keycloak (among others).

3. Create a Client in KeyCloak:
    1. Open KeyCloak UI (usually at http://localhost:8080/) and use admin/admin to login.
    2. Click on the Clients tab and the Create client button
    3. Type a Client ID (can be 'test' by default) and click the Next button. There is no need to enter a name.
    4. Toggle Client Authentication and Authorization to the on position. Click next and save.
    5. After the client is created, click on the Credentials tab. Note the ClientSecret.

4. Open a new terminal and run `dotnet run --project src/TestApi/TestApi.csproj` from the root directory

5. Open a browser and navigate to the OpenId configuration in
   KeyClock (http://localhost:8080/realms/master/.well-known/openid-configuration by default). Check OpenIdConfigUrl in
   src\TestApi\Program.cs if the default doesn't work. Note the URL in the "token_endpoint" field of the json output.

6. Get an authorization token via Postman:
    1. Open Postman
    2. Create a new POST request with the URL set to the "token_endpoint" url in the previous
       step (http://localhost:8080/realms/master/protocol/openid-connect/token by default).
    3. In the Authorization tab choose Type: Basic Auth, and set the Username to the ClientId you created in step 3 and
       the Password to the ClientSecret.
    4. In the Body tab choose form-data and add the following key-value pairs:
       "grant_type" "password"
       "username" "admin"
       "password" "admin"
    5. Click send and note the value in the "access_token" field of the json response

7. Send test requests:
    1. Open a browser and naviate the swagger page for the TestApi project (http://localhost:5218/swagger/index.html by
       default). Check src\TestApi\Properties\launchSettings.json for the URL if the default doesn't work.
    2. Click on Authorize and enter the JWT token from the "access_token" field from Step 6.
    3. Use Swagger or POSTMan to sent test requests and read the response from the Test API
