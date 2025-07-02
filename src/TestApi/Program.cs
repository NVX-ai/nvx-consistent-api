using System.Diagnostics;
using FS.Keycloak.RestApiClient.Api;
using FS.Keycloak.RestApiClient.Authentication.ClientFactory;
using FS.Keycloak.RestApiClient.Authentication.Flow;
using FS.Keycloak.RestApiClient.ClientFactory;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nvx.ConsistentAPI;
using static DeFuncto.Prelude;
using TestModel = TestEventModel.TestEventModel;

const string keycloakBaseUrl = "http://localhost:8080";
var openIdConfigUrl = Environment.GetEnvironmentVariable("OPENID_CONFIG_URL")
                      ?? $"{keycloakBaseUrl}/realms/master/.well-known/openid-configuration";
var sqlConnectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
                          ?? "Server=localhost,1433;Database=master;User Id=sa;Password=yourStrong(!)Password;Encrypt=True;TrustServerCertificate=True";
var eventStoreConnectionString = Environment.GetEnvironmentVariable("EVENTSTORE_CONNECTION_STRING")
                                 ?? "esdb://admin:changeit@localhost:2113?tls=false";
var adminSubjectId = Environment.GetEnvironmentVariable("ADMIN_SUBJECT_ID") ?? await GetAdminSubjectId();
var blobConnectionString = Environment.GetEnvironmentVariable("BLOB_CONNECTION_STRING") ?? "UseDevelopmentStorage=true";

/*
Example of a JWT signing public key in PEM format:
-----BEGIN PUBLIC KEY-----
MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDdlatRjRjogo3WojgGHFHYLugd
UWAY9iR3fy4arWNA1KoS8kVw33cJibXr8bvwUAUparCwlvdbH6dvEOfou0/gCFQs
HUfQrSDv+MuSUMAe8jzKE4qW+jK+xQU9a03GUnKHkkle+Q0pX/g6jXZ7r1/xAK5D
o2kQ+X5xK9cipRgEKwIDAQAB
-----END PUBLIC KEY-----
*/

async Task<string> GetAdminSubjectId()
{
  var credentials = new PasswordGrantFlow
  {
    KeycloakUrl = keycloakBaseUrl,
    UserName = "admin",
    Password = "admin"
  };
  using var httpClient = AuthenticationHttpClientFactory.Create(credentials);
  using var usersApi = ApiClientFactory.Create<UsersApi>(httpClient);
  var stopWatch = Stopwatch.StartNew();
  while (stopWatch.Elapsed.TotalSeconds < 45)
  {
    try
    {
      var users = await usersApi.GetUsersAsync("master");
      if (users.Any())
      {
        return users.Single(u => u.Username == "admin").Id;
      }
    }
    catch
    {
      // If failed to get the users, wait for a while and retry.
      await Task.Delay(100);
    }
  }

  throw new TimeoutException("Failed to get admin user id from Keycloak within 45 seconds.");
}

var app = await Generator.GetWebApp(
  null,
  new GeneratorSettings(
    sqlConnectionString,
    eventStoreConnectionString,
    blobConnectionString,
    await GetPublicKeysAsync(),
    adminSubjectId,
    null,
    None,
    None,
    None,
    new LoggingSettings
    {
      LogsFolder = "logs",
      TracingOpenTelemetryEndpoint = "http://localhost:4317/"
    },
    "TestApiToolingApiKey"
  ),
  TestModel.GetModel(),
  ["http://localhost:4200"]);

app.Run();
return;

async Task<SecurityKey[]> GetPublicKeysAsync()
{
  var openIdConfig = await GetOpenIdConfiguration(openIdConfigUrl);

  using var client = new HttpClient();
  var response = await client.GetStringAsync(openIdConfig.JwksUri);
  var jwks = JsonConvert.DeserializeObject<JObject>(response)!;
  var keys = jwks.Value<JArray>("keys")!;
  return keys.Select(key => new JsonWebKey(key.ToString())).Cast<SecurityKey>().ToArray();
}

async Task<OpenIdConfiguration> GetOpenIdConfiguration(string url)
{
  using var client = new HttpClient();
  var response = await client.GetStringAsync(url);
  return JsonConvert.DeserializeObject<OpenIdConfiguration>(response)!;
}
