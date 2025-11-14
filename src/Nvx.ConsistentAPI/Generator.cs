using System.Reflection;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.Framework.SignalRMessage;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nvx.ConsistentAPI;

public record GeneratorSettings(
  string ReadModelConnectionString,
  string EventStoreConnectionString,
  string BlobStorageConnectionString,
  SecurityKey[] JwtPublicKeys,
  string AdminId,
  string? AzureSignalRConnectionString,
  Option<Action<WebApplicationBuilder>> BuilderCustomizations,
  Option<Action<SwaggerGenOptions>> SwaggerCustomizations,
  Option<Action<WebApplication>> AppCustomizations,
  LoggingSettings LoggingSettings,
  string ToolingEndpointsApiKey,
  FrameworkFeatures EnabledFeatures = FrameworkFeatures.All,
  int ParallelHydration = 25,
  int TodoProcessorWorkerCount = 25);

// ReSharper disable once ClassNeverInstantiated.Global
internal class AllOperationsFilter(EventModel model) : IOperationFilter
{
  public void Apply(OpenApiOperation operation, OperationFilterContext context)
  {
    operation.Tags ??= new List<OpenApiTag>();
    if (!operation.Tags.Any())
    {
      operation.Tags.Add(new OpenApiTag { Name = $"NotTagged{model.ApiName?.Replace(" ", "")}" });
    }
  }
}

// ReSharper disable once ClassNeverInstantiated.Global
internal class RequiredNotNullableSchemaFilter : ISchemaFilter
{
  public void Apply(OpenApiSchema schema, SchemaFilterContext context)
  {
    if (schema.Properties == null)
    {
      return;
    }

    FixNullableProperties(schema, context);

    var notNullableProperties = schema
      .Properties
      .Where(x => !x.Value.Nullable && !schema.Required.Contains(x.Key))
      .ToList();

    foreach (var property in notNullableProperties)
    {
      schema.Required.Add(property.Key);
    }
  }

  /// <summary>
  ///   Option "SupportNonNullableReferenceTypes" not working with complex types ({ "type": "object" }),
  ///   so they always have "Nullable = false",
  ///   see method "SchemaGenerator.GenerateSchemaForMember"
  /// </summary>
  private static void FixNullableProperties(OpenApiSchema schema, SchemaFilterContext context)
  {
    foreach (var property in schema.Properties)
    {
      if (property.Value.Reference == null)
      {
        continue;
      }

      var field = context
        .Type
        .GetMembers(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(x =>
          string.Equals(x.Name, property.Key, StringComparison.InvariantCultureIgnoreCase));

      if (field == null)
      {
        continue;
      }

      var fieldType = field switch
      {
        FieldInfo fieldInfo => fieldInfo.FieldType,
        PropertyInfo propertyInfo => propertyInfo.PropertyType,
        _ => throw new NotSupportedException()
      };

      property.Value.Nullable = fieldType.IsValueType
        ? Nullable.GetUnderlyingType(fieldType) != null
        : !field.IsNonNullableReferenceType();
    }
  }
}

// ReSharper disable once ClassNeverInstantiated.Global
public class EnumSchemaFilter : ISchemaFilter
{
  public void Apply(OpenApiSchema model, SchemaFilterContext context)
  {
    if (!context.Type.IsEnum)
    {
      return;
    }

    model.Type = "string";
    model.Enum.Clear();

    foreach (var n in Enum.GetNames(context.Type))
    {
      model.Enum.Add(new OpenApiString(n));
    }
  }
}

[Flags]
public enum FrameworkFeatures
{
  None = 0,
  ReadModelHydration = 1 << 0,
  ReadModelEndpoints = 1 << 1,
  StaticEndpoints = 1 << 2,
  SystemEndpoints = 1 << 3,
  Commands = 1 << 4,
  Projections = 1 << 5,
  Tasks = 1 << 6,
  Ingestors = 1 << 7,
  SignalR = 1 << 8,

  All = ReadModelHydration
        | ReadModelEndpoints
        | StaticEndpoints
        | SystemEndpoints
        | Commands
        | Projections
        | Tasks
        | Ingestors
        | SignalR
}

public static class FrameworkFeaturesExtensions
{
  public static bool HasEndpoints(this FrameworkFeatures features) =>
    features.HasQueries() || features.HasCommands() || features.HasIngestors();

  public static bool HasQueries(this FrameworkFeatures features) =>
    ((FrameworkFeatures.StaticEndpoints
      | FrameworkFeatures.SystemEndpoints
      | FrameworkFeatures.ReadModelEndpoints)
     & features)
    != FrameworkFeatures.None;

  public static bool HasCommands(this FrameworkFeatures features) =>
    (FrameworkFeatures.Commands & features) != FrameworkFeatures.None;

  public static bool HasIngestors(this FrameworkFeatures features) =>
    (FrameworkFeatures.Ingestors & features) != FrameworkFeatures.None;
}

public static class Generator
{
  /// <summary>
  ///   Creates and configures a ConsistentApp instance.
  /// </summary>
  /// <param name="port">The port for the web application to listen on.</param>
  /// <param name="settings">The settings for the generator.</param>
  /// <param name="eventModel">The event model for the application.</param>
  /// <param name="corsOrigins">The allowed CORS origins.</param>
  /// <returns>A Task that results in a ConsistentApp instance.</returns>
  public static async Task<ConsistentApp> GetWebApp(
    int? port,
    GeneratorSettings settings,
    EventModel eventModel,
    string[] corsOrigins)
  {
    ValidateEventCohesion();
    ValidateStrongIds();
    SqlMapper.RemoveTypeMap(typeof(DateTime));
    SqlMapper.RemoveTypeMap(typeof(DateTime?));
    SqlMapper.RemoveTypeMap(typeof(ulong));
    SqlMapper.RemoveTypeMap(typeof(ulong?));
    SqlMapper.AddTypeHandler(typeof(DateTime), new DateTimeTypeHandler());
    SqlMapper.AddTypeHandler(typeof(DateTime?), new DateTimeTypeHandler());
    SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    SqlMapper.AddTypeHandler(new DateOnlyNullableTypeHandler());
    SqlMapper.AddTypeHandler(new ULongTypeHandler());
    SqlMapper.AddTypeHandler(new ULongNullableTypeHandler());

    Environment.SetEnvironmentVariable("OTEL_SERVICE_NAME", eventModel.ApiName ?? "Nvx.ConsistentAPI");

    var builder = WebApplication.CreateBuilder();

    builder.Logging.SetMinimumLevel(settings.LoggingSettings.LogLevel);

    if (settings.LoggingSettings.AzureInstrumentationKey != null)
    {
      builder.Services.AddApplicationInsightsTelemetry(options =>
      {
        options.ConnectionString = $"InstrumentationKey={settings.LoggingSettings.AzureInstrumentationKey}";
      });
    }

    builder
      .Services
      .AddAuthentication(options =>
      {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
      })
      .AddJwtBearer(options =>
      {
        options.TokenValidationParameters = new TokenValidationParameters
        {
          ValidateIssuer = false,
          ValidateAudience = false,
          ValidateLifetime = true,
          ValidateIssuerSigningKey = false,
          IssuerSigningKeys = settings.JwtPublicKeys
        };

        options.Events = new JwtBearerEvents
        {
          OnMessageReceived = context =>
          {
            var fromQuery = context.Request.Query["access_token"].ToString();
            var fromHeaders = context.Request.Headers.Authorization.ToString();

            var accessToken = string.IsNullOrWhiteSpace(fromQuery)
              ? GetFromHeader(fromHeaders)
              : fromQuery;

            // If the request is for our hub...
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/message-hub"))
            {
              // Read the token out of the query string
              context.Token = accessToken;
            }

            return Task.CompletedTask;
          }
        };
      });
    settings.BuilderCustomizations.Iter(c => c(builder));
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(swaggerGenOptions =>
    {
      swaggerGenOptions.SupportNonNullableReferenceTypes();
      swaggerGenOptions.SchemaFilter<RequiredNotNullableSchemaFilter>();
      swaggerGenOptions.SchemaFilter<EnumSchemaFilter>();
      swaggerGenOptions.OperationFilter<AllOperationsFilter>(eventModel);
      swaggerGenOptions.SwaggerDoc(
        "v1",
        new OpenApiInfo { Title = eventModel.ApiName ?? "Consistent API", Version = eventModel.ApiVersion ?? "v1" });
      var jwtSecurityScheme = new OpenApiSecurityScheme
      {
        BearerFormat = "JWT",
        Name = "JWT Authentication",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        Description = "Put **_ONLY_** your JWT Bearer token on text box below!",
        Reference = new OpenApiReference
          { Id = JwtBearerDefaults.AuthenticationScheme, Type = ReferenceType.SecurityScheme }
      };

      swaggerGenOptions.AddSecurityDefinition(jwtSecurityScheme.Reference.Id, jwtSecurityScheme);
      swaggerGenOptions.AddSecurityRequirement(
        new OpenApiSecurityRequirement { { jwtSecurityScheme, [] } });
      settings.SwaggerCustomizations.Iter(c => c(swaggerGenOptions));
    });

    if (port != null)
    {
      builder.WebHost.UseUrls($"http://localhost:{port}");
    }

    const string corsPolicyName = "AllowDevelopmentCors";
    builder.Services.AddCors(options =>
      options.AddPolicy(
        corsPolicyName,
        policy =>
          policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
      )
    );

    var signalRBuilder = builder.Services.AddSignalR();
    if (settings.AzureSignalRConnectionString is not null)
    {
      signalRBuilder.AddAzureSignalR(settings.AzureSignalRConnectionString);
    }

    var otel = builder.Services.AddOpenTelemetry();
    otel.WithMetrics(metrics =>
    {
      metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
        .AddMeter("System.Net.Http")
        .AddMeter("System.Net.NameResolution")
        .AddMeter(PrometheusMetrics.Source.Name)
        .AddMeter("System.Runtime")
        .AddPrometheusExporter();

      if (settings.LoggingSettings is { UseConsoleLogger: true, AddMetricsAndTracingToConsole: true })
      {
        metrics.AddConsoleExporter();
      }

      if (settings.LoggingSettings.TracingOpenTelemetryEndpoint != null)
      {
        metrics.AddOtlpExporter(otlpOptions =>
        {
          otlpOptions.Endpoint = new Uri(settings.LoggingSettings.TracingOpenTelemetryEndpoint);
        });
      }
    });

    otel.WithTracing(tracing =>
    {
      tracing.AddAspNetCoreInstrumentation();
      tracing.AddHttpClientInstrumentation();
      tracing.AddSource(PrometheusMetrics.Source.Name);
      if (settings.LoggingSettings.TracingOpenTelemetryEndpoint != null)
      {
        tracing.AddOtlpExporter(otlpOptions =>
        {
          otlpOptions.Endpoint = new Uri(settings.LoggingSettings.TracingOpenTelemetryEndpoint);
        });
      }

      if (settings.LoggingSettings is { UseConsoleLogger: true, AddMetricsAndTracingToConsole: true })
      {
        tracing.AddConsoleExporter();
      }
    });

    builder.Services.AddLogging(loggingBuilder =>
    {
      loggingBuilder.AddSerilog(
        new LoggerConfiguration()
          .MinimumLevel
          .Override("Nvx.ConsistentAPI", Map(settings.LoggingSettings.LogLevel))
          .Enrich.FromLogContext()
          .Filter.ByExcluding(logEvent =>
            logEvent.Properties.TryGetValue("RequestPath", out var pathValue) &&
            pathValue.ToString().Contains("/metrics"))
          .WriteTo.Conditional(_ => settings.LoggingSettings.LogsFolder != null,
            wt => wt.File(Path.Combine(settings.LoggingSettings.LogsFolder ?? "./", "log-.log"),
              rollingInterval: settings.LoggingSettings.LogFileRollInterval.ToSerilog(),
              retainedFileTimeLimit: TimeSpan.FromDays(settings.LoggingSettings.LogDaysToKeep)))
          .WriteTo.Conditional(_ => settings.LoggingSettings.UseConsoleLogger
            , wt => wt.Console(restrictedToMinimumLevel: Map(settings.LoggingSettings.LogLevel)))
          .CreateLogger());
    });

    var app = builder.Build();
    app.UseCors(corsPolicyName);
    app.UseRouting();
    app.MapHub<NotificationHub>("/message-hub");
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapPrometheusScrapingEndpoint();
    
    var logger = app.Services.GetRequiredService<ILogger<WebApplication>>();

    var merged =
      FrameworkEventModel
        .Model(settings)
        .Merge(eventModel);

    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.SignalR))
    {
      logger.LogInformation("Signal-R feature is disabled, not adding SignalR messages to the event model.");
    }
    else
    {
      merged.Merge(SignalRMessageSubModel.Get(SendNotificationFunctionBuilder.Build(app.Services.GetRequiredService<IHubContext<NotificationHub>>())));
    }

    VerifyPrefixes(merged);

    var (fetcher, consistencyCheck) = await merged.ApplyTo(app, settings, logger);

    settings.AppCustomizations.Iter(c => c(app));

    app.Use((context, next) =>
    {
      if (
        context.Request.Path.ToString().EndsWith("files/upload")
        && context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { } feature
      )
      {
        feature.MaxRequestBodySize = 350_000_000;
      }

      if (context.Request.Path.ToString().EndsWith("swagger.json"))
      {
        context.Response.Headers.AccessControlAllowOrigin = "*";
      }

      return next.Invoke();
    });

    app.UseSwagger(options =>
    {
      options.PreSerializeFilters.Add((swagger, httpReq) =>
      {
        if (httpReq.Headers.TryGetValue("X-Forwarded-Prefix", out var value))
        {
          swagger.Servers = new List<OpenApiServer> { new() { Url = value } };
        }
      });
    });
    app.UseSwaggerUI();
    return new ConsistentApp(app, fetcher, consistencyCheck);

    LogEventLevel Map(LogLevel level) => level switch
    {
      LogLevel.Trace => LogEventLevel.Verbose,
      LogLevel.Debug => LogEventLevel.Debug,
      LogLevel.Information => LogEventLevel.Information,
      LogLevel.Warning => LogEventLevel.Warning,
      LogLevel.Error => LogEventLevel.Error,
      _ => LogEventLevel.Fatal
    };
  }

  private static void VerifyPrefixes(EventModel merged)
  {
    var prefixes = merged.Prefixes;
    foreach (var prefix in prefixes)
    {
      var overlappingPrefix = prefixes.FirstOrDefault(p => p != prefix && p.StartsWith(prefix));
      if (overlappingPrefix is not null)
      {
        throw new Exception($"Prefix '{overlappingPrefix}' overlaps with '{prefix}'");
      }
    }
  }

  internal static void ValidateEventCohesion()
  {
    var assemblies = AppDomain.CurrentDomain.GetAssemblies();

    var foldTypes = new List<Type>();
    var eventModelEventTypes = new List<Type>();


    foreach (var assembly in assemblies)
    {
      // There is a bug with the test runner that prevents loading some types
      // from system data while running tests.
      if (assembly.FullName?.StartsWith("System.Data.") ?? false)
      {
        continue;
      }

      foldTypes.AddRange(
        assembly
          .GetTypes()
          .Where(type =>
            type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Folds<,>))
          )
      );

      eventModelEventTypes.AddRange(
        assembly
          .GetTypes()
          .Where(type =>
            typeof(EventModelEvent).IsAssignableFrom(type) && !type.IsAbstract
          )
      );
    }

    foreach (var eventModelEventType in eventModelEventTypes)
    {
      var foldCountForEvent = foldTypes.Count(foldType => foldType
        .GetInterfaces()
        .Any(i =>
          i.IsGenericType
          && i.GetGenericTypeDefinition() == typeof(Folds<,>)
          && i.GetGenericArguments()[0] == eventModelEventType
        )
      );

      var isObsolete = eventModelEventType.GetCustomAttribute<ObsoleteAttribute>() is not null;
      if (isObsolete && foldCountForEvent != 0)
      {
        throw new Exception(
          $"The event type '{eventModelEventType.Name}' is marked as obsolete and should not be folded.");
      }

      if (foldCountForEvent != 1 && !isObsolete)
      {
        throw new Exception(
          $"The event type '{eventModelEventType.Name}' is being fold in {foldCountForEvent} entities, expected exactly one.");
      }
    }
  }

  private static void ValidateStrongIds()
  {
    // Strong IDs should override ToString to return StreamId,
    // since there is no sensible way to model this in the type
    // system, this test will scan all StrongId subclasses and
    // verify that the ToString method returns the StreamId.
    // The reason behind this is that it's really easy to write
    // a method to get a stream name by interpolating the StrongId,
    // which will automatically call ToString, and ToString is
    // automatically generated for pretty printing in records,
    // ensuring chaos and brimstone.
    var types = AppDomain
      .CurrentDomain.GetAssemblies()
      .Where(a => !a.FullName?.StartsWith("System.Data.") ?? false)
      .SelectMany(a => a.GetTypes())
      .Where(t => t.IsSubclassOf(typeof(StrongId)));

    foreach (var type in types)
    {
      var constructor = type.GetConstructors().First();
      var parameters = constructor.GetParameters();
      var values =
        from parameter in parameters
        select parameter switch
        {
          _ when parameter.ParameterType == typeof(string) => Guid.NewGuid().ToString(),
          { ParameterType.IsValueType: true } => Activator.CreateInstance(parameter.ParameterType),
          _ => throw new NotSupportedException("Use only primitives for strong id properties.")
        };
      var instance = (StrongId)constructor.Invoke(values.ToArray());
      if (instance.ToString() != instance.StreamId())
      {
        throw new Exception($"StrongId {type.Name} does not override ToString to return StreamId");
      }
    }
  }

  private static string GetFromHeader(string header) => header.StartsWith("Bearer ") ? header[7..] : "";
}
