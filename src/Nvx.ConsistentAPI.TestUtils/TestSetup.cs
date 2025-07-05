using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using Dapper;
using EventStore.Client;
using Flurl;
using Flurl.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Nvx.ConsistentAPI.Framework;
using Nvx.ConsistentAPI.InternalTooling;
using Testcontainers.Azurite;
using Testcontainers.EventStoreDb;
using Testcontainers.MsSql;

namespace Nvx.ConsistentAPI.TestUtils;

public delegate string TestUserByName(string name);

public record TestAuth(string AdminSub, string CandoSub, TestUserByName ByName);

internal static class InstanceTracking
{
  private static readonly SemaphoreSlim Semaphore = new(1);
  private static readonly Dictionary<int, TestSetupHolder> Holders = new();

  internal static async Task<TestSetup> Get(EventModel model, TestSettings? settings = null)
  {
    var hash = model.GetHashCode();
    await Semaphore.WaitAsync();
    if (Holders.TryGetValue(hash, out var h))
    {
      Holders[hash] = h with { Count = h.Count + 1 };
      Semaphore.Release();

      return new TestSetup(
        h.Url,
        h.Auth,
        h.EventStoreClient,
        h.Model,
        (settings ?? new TestSettings()).WaitForCatchUpTimeout);
    }

    var holder = await TestSetup.InitializeInternal(model, settings ?? new TestSettings());
    holder.Logger.LogInformation("Initialized test setup for {Hash}", hash);
    Holders[hash] = holder;
    Semaphore.Release();

    return new TestSetup(
      holder.Url,
      holder.Auth,
      holder.EventStoreClient,
      holder.Model,
      (settings ?? new TestSettings()).WaitForCatchUpTimeout);
  }

  internal static Task Dispose(int _) => Task.CompletedTask;
}

internal record TestSetupHolder(
  string Url,
  TestAuth Auth,
  EventStoreClient EventStoreClient,
  EventModel Model,
  int Count,
  ILogger Logger);

internal record TestUser(string Sub, Claim[] Claims);

public record TestSetup(
  string Url,
  TestAuth Auth,
  EventStoreClient EventStoreClient,
  EventModel Model,
  int WaitForCatchUpTimeout) : IAsyncDisposable
{
  private const string SecretKey = "2EYZvr55gtbEDgVbCqMt2xk2kE7TPrvj";

  private static readonly ConcurrentDictionary<string, TestUser> TestUsers = new();

  private static readonly SemaphoreSlim Semaphore = new(1);

  private static readonly Random Random = new();

  private static readonly SemaphoreSlim InitializerSemaphore = new(1);

  private static readonly SigningCredentials SigningCredentials = new(
    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)),
    SecurityAlgorithms.HmacSha256);

  private static readonly JwtSecurityTokenHandler TokenHandler = new();

  private static readonly ConcurrentDictionary<string, string> Tokens = new();

  private readonly SemaphoreSlim WaitForConsistencySemaphore = new(1);

  private DateTime lastActivityAt = DateTime.UtcNow;

  public async ValueTask DisposeAsync()
  {
    await InstanceTracking.Dispose(Model.GetHashCode());
    GC.SuppressFinalize(this);
  }

  private static TestUser GetTestUser(string name) =>
    TestUsers.GetOrAdd(
      name,
      n => Guid
        .NewGuid()
        .ToString()
        .Apply(sub => new TestUser(
          sub,
          [
            new Claim(JwtRegisteredClaimNames.Sub, sub),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Name, n),
            new Claim(Random.Next() % 2 == 0 ? "emails" : JwtRegisteredClaimNames.Email, $"{n}@testdomain.com")
          ])));

  public async Task InsertEvents(params EventModelEvent[] evt)
  {
    await EventStoreClient.AppendToStreamAsync(
      evt.GroupBy(e => e.GetStreamName()).Single().Key,
      StreamState.Any,
      Emitter.ToEventData(evt, null));
    lastActivityAt = DateTime.UtcNow;
  }

  /// <summary>
  ///   Waits for the system to be in a consistent state.
  /// </summary>
  /// <param name="timeoutMs">Overload of the settings timeout to wait for consistency.</param>
  public async Task WaitForConsistency(int? timeoutMs = null)
  {
    var checkStartedAt = DateTime.UtcNow;
    var timeout = timeoutMs ?? WaitForCatchUpTimeout;
    var timer = Stopwatch.StartNew();
    // Wait until it's inactive and is seen as inconsistent
    while (timer.ElapsedMilliseconds < timeout)
    {
      if (await IsConsistent())
      {
        return;
      }

      await Task.Delay(100);
    }

    // This will let go, but tests are expected to fail if consistency was not reached.
    return;

    bool IsActive() => DateTime.UtcNow - lastActivityAt < TimeSpan.FromSeconds(2);

    async Task<bool> IsConsistent()
    {
      if (IsActive())
      {
        return false;
      }

      try
      {
        await WaitForConsistencySemaphore.WaitAsync();

        var status = await $"{Url}{CatchUp.Route}"
          .WithHeader("Internal-Tooling-Api-Key", "TestApiToolingApiKey")
          .GetJsonAsync<HydrationStatus>();

        var daemonInsights = await $"{Url}{DaemonsInsight.Route}"
          .WithHeader("Internal-Tooling-Api-Key", "TestApiToolingApiKey")
          .GetJsonAsync<DaemonsInsights>();

        var timePassedSinceLastEvent = DateTime.UtcNow - daemonInsights.LastEventEmittedAt;

        var isConsistent =
          status.IsCaughtUp
          && daemonInsights.IsFullyIdle
          && timePassedSinceLastEvent > TimeSpan.FromSeconds(2);
        if (!isConsistent)
        {
          lastActivityAt = DateTime.UtcNow;
        }

        return isConsistent;
      }
      finally
      {
        WaitForConsistencySemaphore.Release();
      }
    }
  }

  public async Task<CommandAcceptedResult> Upload()
  {
    var result = await $"{Url}/files/upload"
      .PostMultipartAsync(multipartContent =>
        multipartContent.AddFile("file", new MemoryStream("banana"u8.ToArray()), "text.txt")
      )
      .ReceiveJson<CommandAcceptedResult>();
    lastActivityAt = DateTime.UtcNow;
    return result;
  }

  public async Task<CommandAcceptedResult> UploadPath(string path)
  {
    var result = await $"{Url}/files/upload"
      .PostMultipartAsync(multipartContent =>
        multipartContent.AddFile("file", path)
      )
      .ReceiveJson<CommandAcceptedResult>();
    lastActivityAt = DateTime.UtcNow;
    return result;
  }

  public async Task DownloadAndComparePath(Guid fileId, string path)
  {
    var localFileContent = await File.ReadAllBytesAsync(path);
    var response = await $"{Url}/files/download/{fileId}"
      .DownloadFileAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
    var downloadedFileContent = await File.ReadAllBytesAsync(response);
    Assert.Equal(localFileContent, downloadedFileContent);
  }

  public async Task DownloadAndCompare(Guid fileId, string expectedContent)
  {
    var response = await $"{Url}/files/download/{fileId}"
      .DownloadFileAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
    Assert.True(await File.ReadAllTextAsync(response) == expectedContent);
  }

  public async Task Ingest<T>(string body, Dictionary<string, string>? headers = null)
  {
    var req = new FlurlRequest($"{Url}/ingestor/{Naming.ToSpinalCase(typeof(T))}");
    foreach (var header in headers ?? new Dictionary<string, string>())
    {
      req.WithHeader(header.Key, header.Value);
    }

    await req.PostStringAsync(body);
    lastActivityAt = DateTime.UtcNow;
  }

  public async Task<CommandAcceptedResult> Command<C>(
    C command,
    bool asAdmin = false,
    Guid? tenantId = null,
    Dictionary<string, string>? headers = null,
    string? asUser = null)
  {
    var hd = headers ?? new Dictionary<string, string>();
    var tenancySegment = tenantId.HasValue ? $"/tenant/{tenantId.Value}" : string.Empty;
    var req = $"{Url}{tenancySegment}/commands/{Naming.ToSpinalCase<C>()}"
      .WithOAuthBearerToken(CreateTestJwt(asAdmin ? "admin" : asUser ?? "cando"));
    foreach (var header in hd)
    {
      req.Headers.Add(header.Key, header.Value);
    }

    var response = await req
      .PostAsync(new StringContent(Serialization.Serialize(command), Encoding.UTF8, "application/json"));
    var result = await response.GetJsonAsync<CommandAcceptedResult>();
    lastActivityAt = DateTime.UtcNow;
    return result;
  }

  public async Task<ErrorResponse> FailingCommand<C>(
    C command,
    int responseCode,
    Guid? tenantId = null,
    bool asAdmin = false,
    string? asUser = null)
  {
    var tenancySegment = tenantId.HasValue ? $"/tenant/{tenantId.Value}" : string.Empty;
    var result = await $"{Url}{tenancySegment}/commands/{Naming.ToSpinalCase<C>()}"
      .WithOAuthBearerToken(CreateTestJwt(asAdmin ? "admin" : asUser ?? "cando"))
      .AllowAnyHttpStatus()
      .PostAsync(new StringContent(Serialization.Serialize(command), Encoding.UTF8, "application/json"));
    Assert.Equal(responseCode, result.StatusCode);
    return await result.GetJsonAsync<ErrorResponse>();
  }

  public async Task UnauthorizedCommand<C>(C command) =>
    Assert.Equal(
      (int)HttpStatusCode.Unauthorized,
      (await $"{Url}/commands/{Naming.ToSpinalCase<C>()}"
        .AllowAnyHttpStatus()
        .PostAsync(new StringContent(Serialization.Serialize(command), Encoding.UTF8, "application/json")))
      .StatusCode);

  public async Task ForbiddenCommand<C>(C command) =>
    Assert.Equal(
      (int)HttpStatusCode.Forbidden,
      (await $"{Url}/commands/{Naming.ToSpinalCase<C>()}"
        .WithOAuthBearerToken(CreateTestJwt("nocando"))
        .AllowAnyHttpStatus()
        .PostAsync(new StringContent(Serialization.Serialize(command), Encoding.UTF8, "application/json")))
      .StatusCode);

  public async Task<UserSecurity> CurrentUser(bool asAdmin = false, string asUser = "cando")
  {
    await WaitForConsistency();
    return await $"{Url}/current-user"
      .WithOAuthBearerToken(CreateTestJwt(asAdmin ? "admin" : asUser))
      .GetJsonAsync<UserSecurity>();
  }

  public async Task<PageResult<Rm>> ReadModels<Rm>(
    bool asAdmin = false,
    Guid? tenantId = null,
    Dictionary<string, string[]>? queryParameters = null,
    string asUser = "cando")
  {
    await WaitForConsistency();
    var tenancySegment = tenantId.HasValue ? $"/tenant/{tenantId.Value}" : string.Empty;
    var result = await (queryParameters ?? new Dictionary<string, string[]>())
      .Aggregate(
        new Url($"{Url}{tenancySegment}/read-models/{Naming.ToSpinalCase<Rm>()}"),
        (current, kvp) => current.SetQueryParam(kvp.Key, kvp.Value))
      .WithOAuthBearerToken(CreateTestJwt(asAdmin ? "admin" : asUser))
      .GetStringAsync();
    return Serialization.Deserialize<PageResult<Rm>>(result)!;
  }

  public async Task<Rm> ReadModel<Rm>(
    string id,
    Dictionary<string, string[]>? queryParameters = null,
    bool asAdmin = false,
    Guid? tenantId = null,
    string? asUser = null)
  {
    await WaitForConsistency();
    var tenancySegment = tenantId.HasValue ? $"/tenant/{tenantId.Value}" : string.Empty;
    var result = await (queryParameters ?? new Dictionary<string, string[]>())
      .Aggregate(
        new Url($"{Url}{tenancySegment}/read-models/{Naming.ToSpinalCase<Rm>()}/{Flurl.Url.Encode(id)}"),
        (current, kvp) => current.SetQueryParam(kvp.Key, kvp.Value))
      .WithOAuthBearerToken(CreateTestJwt(asAdmin ? "admin" : asUser ?? "cando"))
      .GetStringAsync();
    return Serialization.Deserialize<Rm>(result)!;
  }

  public async Task ResetReadModel<Rm>() =>
    await $"{Url}/reset-read-model/{Naming.ToSpinalCase<Rm>()}"
      .WithOAuthBearerToken(CreateTestJwt("admin"))
      .GetAsync();

  public async Task ReadModelNotFound<Rm>(string id, Guid? tenantId = null, bool asAdmin = false, string? asUser = null)
  {
    await WaitForConsistency();
    var tenancySegment = tenantId.HasValue ? $"/tenant/{tenantId.Value}" : string.Empty;
    var response = await $"{Url}{tenancySegment}/read-models/{Naming.ToSpinalCase<Rm>()}/{id}"
      .WithOAuthBearerToken(CreateTestJwt(asAdmin ? "admin" : asUser ?? "cando"))
      .AllowAnyHttpStatus()
      .GetAsync();
    Assert.Equal(404, response.StatusCode);
  }

  public async Task ForbiddenReadModel<Rm>(Guid? tenantId = null)
  {
    await WaitForConsistency();
    var tenancySegment = tenantId.HasValue ? $"/tenant/{tenantId.Value}" : string.Empty;
    var response = await $"{Url}{tenancySegment}/read-models/{Naming.ToSpinalCase<Rm>()}"
      .WithOAuthBearerToken(CreateTestJwt("nocando"))
      .AllowAnyHttpStatus()
      .GetAsync();
    Assert.Equal((int)HttpStatusCode.Forbidden, response.StatusCode);
  }

  public async Task<Shape?> StaticEndpoint<Shape>(
    bool asAdmin = false,
    HttpStatusCode expectedStatusCode = HttpStatusCode.OK,
    string asUser = "cando") where Shape : class
  {
    var response = await new Url($"{Url}/static/{Naming.ToSpinalCase<Shape>()}")
      .WithOAuthBearerToken(CreateTestJwt(asAdmin ? "admin" : asUser))
      .GetAsync();
    Assert.Equal(200, (int)expectedStatusCode);
    return response.StatusCode == 200 ? await response.GetJsonAsync<Shape?>() : null;
  }

  public async Task<IFlurlResponse> RawPost(string path, string body, bool asAdmin = false, string asUser = "cando")
  {
    var requestContent = new StringContent(body, Encoding.UTF8, "application/json");
    return await $"{Url}{path}"
      .WithOAuthBearerToken(CreateTestJwt(asAdmin ? "admin" : asUser))
      .AllowAnyHttpStatus()
      .PostAsync(requestContent);
  }

  private static async Task<(EventStoreClient client, string esCs)> AwaitEventStore(TestSettings settings)
  {
    var builder = new EventStoreDbBuilder()
      .WithImage(settings.EsDbImage)
      .WithReuse(settings.UsePersistentTestContainers)
      .WithAutoRemove(!settings.UsePersistentTestContainers);

    if (settings.UsePersistentTestContainers)
    {
      builder = builder
        .WithName("consistent-api-integration-test-es")
        .WithEnvironment("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", "true")
        .WithPortBinding(3112, 2113);
    }
    else
    {
      builder = builder.WithEnvironment("EVENTSTORE_MEM_DB", "True");
    }

    var esContainer = builder.Build();

    await esContainer.StartAsync();
    var esCs = esContainer.GetConnectionString();
    var client = new EventStoreClient(EventStoreClientSettings.Create(esCs));
    var timer = Stopwatch.StartNew();
    while (true)
    {
      try
      {
        await Task.Delay(25);
        _ = await client.ReadStreamAsync(Direction.Forwards, "meh", StreamPosition.Start).ReadState;
        return (client, esCs);
      }
      catch (Exception)
      {
        if (timer.ElapsedMilliseconds >= 10_000)
        {
          throw new Exception("Could not connect to EventStore.");
        }
      }
    }
  }

  private static async Task<string> AwaitSqlServer(TestSettings settings)
  {
    var builder = new MsSqlBuilder()
      .WithImage(settings.MsSqlDbImage)
      .WithReuse(settings.UsePersistentTestContainers)
      .WithAutoRemove(!settings.UsePersistentTestContainers);

    if (settings.UsePersistentTestContainers)
    {
      builder = builder.WithName("consistent-api-integration-test-mssql").WithPortBinding(1344, 1433);
    }

    var msSqlContainer = builder.Build();

    await msSqlContainer.StartAsync();
    var cs = msSqlContainer.GetConnectionString();
    var timer = Stopwatch.StartNew();


    while (!settings.UsePersistentTestContainers)
    {
      try
      {
        await msSqlContainer.ExecScriptAsync("ALTER SERVER CONFIGURATION SET MEMORY_OPTIMIZED TEMPDB_METADATA = ON;");
        await msSqlContainer.ExecAsync(["sudo systemctl restart mssql-server"]);
        break;
      }
      catch (Exception)
      {
        if (timer.ElapsedMilliseconds >= 10_000)
        {
          throw new Exception("Could not connect to SQL Server.");
        }

        await Task.Delay(25);
      }
    }

    while (true)
    {
      try
      {
        await using var connection = new SqlConnection(cs);
        _ = await connection.QueryFirstAsync<DateTime>("SELECT GETDATE();");

        return cs;
      }
      catch (Exception)
      {
        if (timer.ElapsedMilliseconds >= 10_000)
        {
          throw new Exception("Could not connect to SQL Server.");
        }

        await Task.Delay(25);
      }
    }
  }

  private static int GetRandomPort() => Random.Next(30000) + 10000;

  private static async Task<int> GetFreePort()
  {
    await Semaphore.WaitAsync();

    try
    {
      int Go(int pn)
      {
        var isTaken = GetConnectionInfo().Any(ci => ci.LocalEndPoint.Port == pn);
        return isTaken ? Go(GetRandomPort()) : pn;
      }

      return Go(GetRandomPort());
    }
    finally
    {
      Semaphore.Release();
    }
  }

  // ReSharper disable once ReturnTypeCanBeEnumerable.Local
  private static TcpConnectionInformation[] GetConnectionInfo() =>
    IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();

  public static async Task<TestSetup> Initialize(EventModel model, TestSettings? settings = null) =>
    await InstanceTracking.Get(model, settings);

  internal static async Task<TestSetupHolder> InitializeInternal(EventModel model, TestSettings settings)
  {
    var azTask = CreateAzurite(settings);
    var sqlTask = AwaitSqlServer(settings);
    var esTask = AwaitEventStore(settings);
    var azuriteConnectionString = await azTask;
    var sqlCs = await sqlTask;
    var (eventStoreClient, esCs) = await esTask;

    var sitePort = await GetFreePort();
    var baseUrl = $"http://localhost:{sitePort}";
    await InitializerSemaphore.WaitAsync(10_000);
    var app = await Generator.GetWebApp(
      sitePort,
      new GeneratorSettings(
        sqlCs,
        esCs,
        azuriteConnectionString,
        CreateTestSecurityKey(),
        GetTestUser("admin").Sub,
        null,
        None,
        None,
        None,
        new LoggingSettings
        {
          LogsFolder = settings.LogsFolder,
          LogFileRollInterval = LogFileRollInterval.Day
        },
        "TestApiToolingApiKey",
        FrameworkFeatures.All,
        settings.HydrationParallelism),
      model,
      ["http://localhost:4200"]);
    try
    {
      InitializerSemaphore.Release();
    }
    catch
    {
      /* ignored */
    }

    await app.StartAsync();

    return new TestSetupHolder(
      baseUrl,
      new TestAuth(GetTestUser("admin").Sub, GetTestUser("cando").Sub, n => GetTestUser(n).Sub),
      eventStoreClient,
      model,
      1,
      app.Services.GetRequiredService<ILogger<TestSetup>>());
  }

  private static async Task<string> CreateAzurite(TestSettings settings)
  {
    var builder = new AzuriteBuilder()
      .WithImage(settings.AzuriteImage)
      .WithReuse(settings.UsePersistentTestContainers)
      .WithAutoRemove(!settings.UsePersistentTestContainers);

    if (settings.UsePersistentTestContainers)
    {
      builder = builder
        .WithName("consistent-api-integration-test-azurite")
        .WithPortBinding(11000, 10000)
        .WithPortBinding(11001, 10001)
        .WithPortBinding(11002, 10002);
    }

    var azuriteContainer = builder.Build();

    await azuriteContainer.StartAsync();
    return azuriteContainer.GetConnectionString();
  }

  private string CreateTestJwt(string name) =>
    Tokens.GetOrAdd(
      name,
      n => TokenHandler.WriteToken(
        new JwtSecurityToken(
          claims: GetTestUser(n).Claims,
          expires: DateTime.UtcNow.AddHours(1),
          signingCredentials: SigningCredentials
        )));

  private static SecurityKey[] CreateTestSecurityKey() => [SigningCredentials.Key];
}

public class TestSettings
{
  private readonly string? azuriteImage;
  private readonly string? esDbImage;
  private readonly string? msSqlDbImage;
  public string? LogsFolder { get; init; }
  public bool UsePersistentTestContainers { get; init; }
  public int WaitForCatchUpTimeout { get; init; } = 150_000;
  public int HydrationParallelism { get; init; } = 5;

  public string EsDbImage
  {
    get => esDbImage ?? EventStoreDefaultConnectionString;
    // ReSharper disable once UnusedMember.Global
    init => esDbImage = value;
  }

  public string MsSqlDbImage
  {
    get => msSqlDbImage ?? MsSqlDefaultConnectionString;
    // ReSharper disable once UnusedMember.Global
    init => msSqlDbImage = value;
  }

  public string AzuriteImage
  {
    get => azuriteImage ?? AzuriteDefaultConnectionString;
    // ReSharper disable once UnusedMember.Global
    init => azuriteImage = value;
  }

  private static string EventStoreDefaultConnectionString =>
    RuntimeInformation.ProcessArchitecture == Architecture.Arm64
    && RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
      ? "eventstore/eventstore:23.10.0-alpha-arm64v8"
      : "eventstore/eventstore:23.10.0-jammy";

  private static string MsSqlDefaultConnectionString =>
    RuntimeInformation.ProcessArchitecture == Architecture.Arm64
    && RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
      ? "mcr.microsoft.com/mssql/server:2019-CU28-ubuntu-20.04"
      : "mcr.microsoft.com/mssql/server:2022-latest";

  private static string AzuriteDefaultConnectionString =>
    RuntimeInformation.ProcessArchitecture == Architecture.Arm64
    && RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
      ? "mcr.microsoft.com/azure-storage/azurite:3.34.0-arm64"
      : "mcr.microsoft.com/azure-storage/azurite:3.34.0";
}
