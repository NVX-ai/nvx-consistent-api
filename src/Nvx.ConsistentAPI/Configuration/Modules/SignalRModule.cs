using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Nvx.ConsistentAPI.Configuration.Modules;

/// <summary>
/// Module that configures SignalR hubs and Azure SignalR Service.
/// </summary>
public class SignalRModule : IGeneratorModule
{
    public int Order => 40;

    public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel)
    {
        var signalRBuilder = builder.Services.AddSignalR();
        if (settings.AzureSignalRConnectionString is not null)
        {
            signalRBuilder.AddAzureSignalR(settings.AzureSignalRConnectionString);
        }
    }

    public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel)
    {
        app.MapHub<NotificationHub>("/message-hub");
    }
}
