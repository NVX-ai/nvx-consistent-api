using Microsoft.AspNetCore.Builder;

namespace Nvx.ConsistentAPI;

public sealed class ConsistentApp(WebApplication webApp, Fetcher fetcher) : IAsyncDisposable
{
  public WebApplication WebApp { get; } = webApp;
  public Fetcher Fetcher { get; } = fetcher;

  public async ValueTask DisposeAsync() => await WebApp.DisposeAsync();

  public void Run() => WebApp.Run();
  public async Task StartAsync() => await WebApp.StartAsync();
}
