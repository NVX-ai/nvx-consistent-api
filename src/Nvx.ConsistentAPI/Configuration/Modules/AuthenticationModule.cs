using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Nvx.ConsistentAPI.Configuration.Modules;

/// <summary>
/// Module that configures JWT Bearer authentication.
/// </summary>
public class AuthenticationModule : IGeneratorModule
{
    public int Order => 10;

    public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel)
    {
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
    }

    public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    private static string GetFromHeader(string header) => header.StartsWith("Bearer ") ? header[7..] : "";
}
