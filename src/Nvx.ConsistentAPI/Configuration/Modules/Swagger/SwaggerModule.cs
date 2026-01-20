using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Nvx.ConsistentAPI.Configuration.Modules.Swagger;

/// <summary>
/// Module that configures Swagger/OpenAPI documentation.
/// </summary>
public class SwaggerModule : IGeneratorModule
{
    public int Order => 20;

    public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel)
    {
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
    }

    public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel)
    {
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
    }
}
