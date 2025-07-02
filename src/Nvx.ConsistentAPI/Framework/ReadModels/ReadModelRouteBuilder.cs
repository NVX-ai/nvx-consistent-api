using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Nvx.ConsistentAPI.Framework;

namespace Nvx.ConsistentAPI;

public record HydrationStatus(bool IsCaughtUp);

public static class ReadModelRouteBuilder
{
  internal static void Apply<Shape>(
    Fetcher fetcher,
    DatabaseHandler<Shape> databaseHandler,
    Emitter emitter,
    GeneratorSettings settings,
    AuthOptions auth,
    WebApplication app,
    EtagHolder etagHolder,
    Action<OpenApiOperation> openApiCustomizer,
    string areaTag,
    Func<Task> resetClosure,
    bool isExposed,
    InternalCustomFilter internalCustomFilter,
    ReadModelDefaulter<Shape> defaulter,
    ILogger logger)
    where Shape : EventModelReadModel
  {
    var isTenantBound = typeof(Shape).GetInterfaces().Contains(typeof(IsTenantBound));
    var routeSegment = Naming.ToSpinalCase<Shape>();
    ApplyResetRoute<Shape>(
      fetcher,
      emitter,
      settings,
      routeSegment,
      app,
      resetClosure,
      logger);

    if (!isExposed)
    {
      return;
    }

    ApplyFindRoute(
      fetcher,
      databaseHandler,
      emitter,
      settings,
      auth,
      routeSegment,
      app,
      isTenantBound,
      etagHolder,
      openApiCustomizer,
      areaTag,
      internalCustomFilter,
      defaulter,
      logger);
    ApplyListRoute(
      fetcher,
      databaseHandler,
      emitter,
      settings,
      auth,
      routeSegment,
      app,
      isTenantBound,
      etagHolder,
      openApiCustomizer,
      areaTag,
      internalCustomFilter,
      logger);
  }

  private static void ApplyFindRoute<Shape>(
    Fetcher fetcher,
    DatabaseHandler<Shape> databaseHandler,
    Emitter emitter,
    GeneratorSettings settings,
    AuthOptions auth,
    string routeSegment,
    WebApplication app,
    bool isTenantBound,
    EtagHolder etagHolder,
    Action<OpenApiOperation> openApiCustomizer,
    string areaTag,
    InternalCustomFilter internalCustomFilter,
    ReadModelDefaulter<Shape> defaulter,
    ILogger logger)
    where Shape : EventModelReadModel
  {
    if (!settings.EnabledFeatures.HasQueries())
    {
      return;
    }

    var route = isTenantBound
      ? $"/tenant/{{tenantId:Guid}}/read-models/{routeSegment}/{{id}}"
      : $"/read-models/{routeSegment}/{{id}}";

    app
      .MapGet(
        route,
        GetForOne(
          fetcher,
          databaseHandler,
          emitter,
          settings,
          auth,
          isTenantBound,
          typeof(Shape)
            .GetInterfaces()
            .Any(i => i.Name == nameof(MultiTenantReadModel) && i.Namespace == typeof(MultiTenantReadModel).Namespace),
          etagHolder,
          internalCustomFilter,
          defaulter,
          logger))
      .WithName(routeSegment)
      .Produces<Shape>()
      .Produces<ErrorResponse>(404)
      .Produces<ErrorResponse>(500)
      .WithOpenApi(o =>
      {
        o.OperationId = $"get{typeof(Shape).Name}".Replace("ReadModel", "");
        o.Tags = [new OpenApiTag { Name = areaTag }];
        openApiCustomizer(o);
        return o;
      })
      .ApplyAuth(auth);
  }

  private static void ApplyListRoute<Shape>(
    Fetcher fetcher,
    DatabaseHandler<Shape> databaseHandler,
    Emitter emitter,
    GeneratorSettings settings,
    AuthOptions auth,
    string routeSegment,
    WebApplication app,
    bool isTenantBound,
    EtagHolder etagHolder,
    Action<OpenApiOperation> openApiCustomizer,
    string areaTag,
    InternalCustomFilter internalCustomFilter,
    ILogger logger)
    where Shape : HasId
  {
    if (!settings.EnabledFeatures.HasQueries())
    {
      return;
    }

    var route = isTenantBound
      ? $"/tenant/{{tenantId:Guid}}/read-models/{routeSegment}"
      : $"/read-models/{routeSegment}";

    var filters = ReadModelFilter.Get(typeof(Shape));

    app
      .MapGet(
        route,
        GetForAll(
          fetcher,
          databaseHandler,
          emitter,
          settings,
          auth,
          isTenantBound,
          typeof(Shape)
            .GetInterfaces()
            .Any(i => i.Name == nameof(MultiTenantReadModel) && i.Namespace == typeof(MultiTenantReadModel).Namespace),
          etagHolder,
          internalCustomFilter,
          logger))
      .WithName($"list{routeSegment}")
      .Produces<PageResult<Shape>>()
      .Produces<ErrorResponse>(500)
      .WithOpenApi(o =>
      {
        o.Parameters.Add(
          new OpenApiParameter
          {
            In = ParameterLocation.Header,
            Name = "If-None-Match",
            Required = false,
            Schema = new OpenApiSchema { Type = "string" }
          });
        foreach (var response in o.Responses.Where(r => r.Key == "200"))
        {
          response
            .Value
            .Headers
            .Add(
              "ETag",
              new OpenApiHeader
                { Description = "The ETag of the response", Schema = new OpenApiSchema { Type = "string" } });
        }

        o.Parameters.Add(
          new OpenApiParameter
          {
            In = ParameterLocation.Query,
            Name = "sortField",
            Required = false,
            Schema = new OpenApiSchema { Type = "string", Enum = GetSortableFields().ToList() },
            Style = ParameterStyle.Form,
            Explode = false
          });
        o.Parameters.Add(
          new OpenApiParameter
          {
            In = ParameterLocation.Query,
            Name = "sortDirection",
            Required = false,
            Schema = new OpenApiSchema
            {
              Type = "string",
              Enum = new List<IOpenApiAny> { new OpenApiString("ascending"), new OpenApiString("descending") }
            },
            Style = ParameterStyle.Form,
            Explode = false
          });
        o.Parameters.Add(
          new OpenApiParameter
          {
            In = ParameterLocation.Query,
            Name = "pageNumber",
            Required = false,
            Schema = new OpenApiSchema { Type = "integer" },
            Style = ParameterStyle.Form,
            Explode = false
          });
        o.Parameters.Add(
          new OpenApiParameter
          {
            In = ParameterLocation.Query,
            Name = "pageSize",
            Required = false,
            Schema = new OpenApiSchema { Type = "integer" },
            Style = ParameterStyle.Form,
            Explode = false
          });
        foreach (var filter in filters)
        {
          o.Parameters.Add(
            new OpenApiParameter
            {
              In = ParameterLocation.Query,
              Name = filter.Key,
              Description = filter.Description,
              Required = false,
              Schema = filter.Schema,
              Style = ParameterStyle.Form,
              Explode = false
            });
        }

        o.OperationId = $"list{typeof(Shape).Name}".Replace("ReadModel", "");
        o.Tags = [new OpenApiTag { Name = areaTag }];
        openApiCustomizer(o);
        return o;
      })
      .ApplyAuth(auth);

    return;

    IEnumerable<IOpenApiAny> GetSortableFields()
    {
      foreach (var prop in typeof(Shape).GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        if (DatabaseHandler<Shape>.MapToSqlType(prop.PropertyType, prop.Name) == "OBJECT")
        {
          continue;
        }

        yield return new OpenApiString(prop.Name);
      }
    }
  }

  private static void ApplyResetRoute<Shape>(
    Fetcher fetcher,
    Emitter emitter,
    GeneratorSettings settings,
    string routeSegment,
    WebApplication app,
    Func<Task> resetClosure,
    ILogger logger) where Shape : HasId
  {
    if (!settings.EnabledFeatures.HasFlag(FrameworkFeatures.ReadModelHydration)
        || !settings.EnabledFeatures.HasFlag(FrameworkFeatures.SystemEndpoints))
    {
      return;
    }

    Delegate resetDelegate = async (HttpContext context) =>
    {
      await FrameworkSecurity
        .Authorization(context, fetcher, emitter, settings, new PermissionsRequireOne("admin"), None)
        .Iter(
          async _ =>
          {
            logger.LogInformation("Resetting read model {ReadModelType}", typeof(Shape));
            await resetClosure();
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(new ReadModelResetResult(true));
          },
          async e => await e.Respond(context));
    };

    app
      .MapGet($"/reset-read-model/{routeSegment}", resetDelegate)
      .WithName($"reset{routeSegment}")
      .Produces<ReadModelResetResult>()
      .WithDescription(
        "Recreates the table tracking the read model, and resubscribes to the event stream immediately")
      .WithOpenApi(o =>
      {
        o.Tags = [new OpenApiTag { Name = OperationTags.FrameworkManagement }];
        o.OperationId = $"reset{typeof(Shape).Name}";
        return o;
      })
      .ApplyAuth(new PermissionsRequireOne("admin"));
  }

  private static Delegate GetForAll<Shape>(
    Fetcher fetcher,
    DatabaseHandler<Shape> databaseHandler,
    Emitter emitter,
    GeneratorSettings settings,
    AuthOptions auth,
    bool isTenantBound,
    bool isMultiTenant,
    EtagHolder holder,
    InternalCustomFilter internalCustomFilter,
    ILogger logger)
    where Shape : HasId =>
    isTenantBound
      ? async (HttpContext context, Guid tenantId) =>
      {
        if (context.Request.Headers.TryGetValue("If-None-Match", out var value) && value.ToString() == holder.Etag)
        {
          context.Response.StatusCode = StatusCodes.Status304NotModified;
          return;
        }

        // Keep it as a statement lambda, this is not meant to return the `Unit` value, but typing `Respond` as
        // `Task<Unit>` gives a performance gain (tiny, but pretty much free).
        await ProcessAll(
            context,
            Some(tenantId),
            false,
            fetcher,
            databaseHandler,
            emitter,
            settings,
            auth,
            internalCustomFilter,
            logger)
          .Async()
          .Respond(context, holder);
      }
      : async (HttpContext context) =>
      {
        if (context.Request.Headers.TryGetValue("If-None-Match", out var value) && value.ToString() == holder.Etag)
        {
          context.Response.StatusCode = StatusCodes.Status304NotModified;
          return;
        }

        // Keep it as a statement lambda, this is not meant to return the `Unit` value, but typing `Respond` as
        // `Task<Unit>` gives a performance gain (tiny, but pretty much free).
        await ProcessAll(
            context,
            None,
            isMultiTenant,
            fetcher,
            databaseHandler,
            emitter,
            settings,
            auth,
            internalCustomFilter,
            logger)
          .Async()
          .Respond(context, holder);
      };

  private static Delegate GetForOne<Shape>(
    Fetcher fetcher,
    DatabaseHandler<Shape> databaseHandler,
    Emitter emitter,
    GeneratorSettings settings,
    AuthOptions auth,
    bool isTenantBound,
    bool isMultiTenant,
    EtagHolder holder,
    InternalCustomFilter internalCustomFilter,
    ReadModelDefaulter<Shape> defaulter,
    ILogger logger)
    where Shape : HasId =>
    isTenantBound
      ? async (HttpContext context, Guid tenantId, string id) =>
      {
        if (context.Request.Headers.TryGetValue("If-None-Match", out var value) && value.ToString() == holder.Etag)
        {
          context.Response.StatusCode = StatusCodes.Status304NotModified;
          return;
        }

        // Keep it as a statement lambda, this is not meant to return the `Unit` value, but typing `Respond` as
        // `Task<Unit>` gives a performance gain (tiny, but pretty much free).
        await ProcessOne(
            id,
            context,
            Some(tenantId),
            false,
            fetcher,
            databaseHandler,
            emitter,
            settings,
            auth,
            internalCustomFilter,
            defaulter,
            logger)
          .Respond(context, holder);
      }
      : async (HttpContext context, string id) =>
      {
        if (context.Request.Headers.TryGetValue("If-None-Match", out var value) && value.ToString() == holder.Etag)
        {
          context.Response.StatusCode = StatusCodes.Status304NotModified;
          return;
        }

        // Keep it as a statement lambda, this is not meant to return the `Unit` value, but typing `Respond` as
        // `Task<Unit>` gives a performance gain (tiny, but pretty much free).
        await ProcessOne(
            id,
            context,
            None,
            isMultiTenant,
            fetcher,
            databaseHandler,
            emitter,
            settings,
            auth,
            internalCustomFilter,
            defaulter,
            logger)
          .Respond(context, holder);
      };

  private static Task<Unit> Respond<T>(this AsyncResult<T, ApiError> self, HttpContext context, EtagHolder holder) =>
    self
      .Iter(
        async value =>
        {
          context.Response.StatusCode = StatusCodes.Status200OK;
          context.Response.Headers.ETag = holder.Etag;
          context.Response.ContentType = "application/json";
          await context.Response.WriteAsync(Serialization.Serialize(value), Encoding.UTF8);
        },
        async error => await error.Respond(context));

  private static async Task<Result<PageResult<Shape>, ApiError>> ProcessAll<Shape>(
    HttpContext context,
    Option<Guid> tenantId,
    bool isMultiTenant,
    Fetcher fetcher,
    DatabaseHandler<Shape> databaseHandler,
    Emitter emitter,
    GeneratorSettings settings,
    AuthOptions auth,
    InternalCustomFilter internalCustomFilter,
    ILogger logger)
    where Shape : HasId
  {
    try
    {
      if (isMultiTenant)
      {
        return await (
          from authorize in FrameworkSecurity.MultiTenantAuthorization(context, fetcher, emitter, settings, auth)
          from page in databaseHandler
            .GetPage(
              GetPageNumber(),
              GetPageSize(),
              context.Request.Query,
              authorize.Map(a => a.user),
              GetSortBy(),
              authorize.Map(a => a.tenants).DefaultValue([]),
              internalCustomFilter(authorize.Map(a => a.user), tenantId))
            .Map(Ok<PageResult<Shape>, ApiError>)
            .Async()
          select page);
      }

      return await (
        from authorize in FrameworkSecurity.Authorization(context, fetcher, emitter, settings, auth, tenantId)
        from page in databaseHandler
          .GetPage(
            GetPageNumber(),
            GetPageSize(),
            context.Request.Query,
            authorize,
            GetSortBy(),
            tenantId.Match<Du3<Guid, Guid[], Unit>>(id => id, () => unit),
            internalCustomFilter(authorize, tenantId))
          .Map(Ok<PageResult<Shape>, ApiError>)
          .Async()
        select page);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Error requesting read model {ReadModelType}", typeof(Shape));
      return new DisasterError("Error processing request").Apply(Error<PageResult<Shape>, ApiError>);
    }

    int GetPageNumber() =>
      context.Request.Query.ContainsKey("pageNumber")
      && int.TryParse(context.Request.Query["pageNumber"], out var parsedPageNumber)
        ? parsedPageNumber
        : 0;

    SortBy? GetSortBy() =>
      context.Request.Query.TryGetValue("sortField", out var values)
        ? new SortBy(values.ToString(), GetDirection())
        : null;

    SortDirection GetDirection() =>
      !context.Request.Query.TryGetValue("sortDirection", out var values)
        ? SortDirection.Ascending
        : values
          .ToString()
          .StartsWith("desc", StringComparison.InvariantCultureIgnoreCase)
          ? SortDirection.Descending
          : SortDirection.Ascending;

    int GetPageSize() =>
      Math.Clamp(
        context.Request.Query.TryGetValue("pageSize", out var value)
        && int.TryParse(value, out var parsedPageSize)
          ? parsedPageSize
          : 50,
        5,
        200);
  }

  private static AsyncResult<Shape, ApiError> ProcessOne<Shape>(
    string id,
    HttpContext context,
    Option<Guid> tenantId,
    bool isMultiTenant,
    Fetcher fetcher,
    DatabaseHandler<Shape> databaseHandler,
    Emitter emitter,
    GeneratorSettings settings,
    AuthOptions auth,
    InternalCustomFilter internalCustomFilter,
    ReadModelDefaulter<Shape> defaulter,
    ILogger logger)
    where Shape : HasId
  {
    return
      isMultiTenant
        ? from authorize in FrameworkSecurity.MultiTenantAuthorization(context, fetcher, emitter, settings, auth)
        from result in Go(authorize, authorize.Map(a => a.tenants).DefaultValue([])).Async()
        select result
        : from authorize in FrameworkSecurity.Authorization(context, fetcher, emitter, settings, auth, tenantId)
        from result in Go(authorize, tenantId.Match<Du3<Guid, Guid[], Unit>>(tid => tid, () => unit)).Async()
        select result;

    async Task<Result<Shape, ApiError>> Go(
      Du<Option<UserSecurity>, Option<(UserSecurity, Guid[])>> userUnion,
      Du3<Guid, Guid[], Unit> tenancy)
    {
      try
      {
        var user = userUnion.Match(Id, o => o.Map(t => t.Item1));
        var customFilter = internalCustomFilter(user, tenantId);
        var result = await databaseHandler.Find(id, tenancy, user, customFilter);
        return result
          .Apply(Optional)
          .BindNone(() => defaulter(id, user, tenantId))
          .Match<Result<Shape, ApiError>>(r => r, () => new NotFoundError(typeof(Shape).Name, id));
      }
      catch (Exception e)
      {
        logger.LogError(
          e,
          "Error requesting read model {ReadModelType} with id {Id}",
          typeof(Shape).Name,
          id);
        return new DisasterError("Error processing request");
      }
    }
  }
}

public class EtagHolder
{
  public string Etag { get; set; } = Guid.NewGuid().ToString();
}

public record ReadModelResetResult(bool Success);

public class UtcDateTimeConverter : JsonConverter<DateTime>
{
  public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
    reader.GetDateTime();

  public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
    writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz"));
}

public record CustomFilter(
  string? JoinClause,
  string[] WhereClauses,
  string? AdditionalColumns,
  bool OverrideTenantFilter = false);

public delegate CustomFilter BuildCustomFilter(
  Option<UserSecurity> user,
  Option<Guid> tenantId,
  ReadModelDetailsFactory factory);

internal delegate CustomFilter InternalCustomFilter(Option<UserSecurity> user, Option<Guid> tenantId);
