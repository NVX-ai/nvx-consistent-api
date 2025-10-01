using Microsoft.AspNetCore.Builder;
using Nvx.ConsistentAPI.InternalTooling;

namespace Nvx.ConsistentAPI;

public sealed class ConsistentApp(
  WebApplication webApp,
  Fetcher fetcher,
  ConsistencyCheck consistencyCheck,
  EventModel.EventParser parser) : IAsyncDisposable
{
  public WebApplication WebApp { get; } = webApp;
  public Fetcher Fetcher { get; } = fetcher;
  public EventModel.EventParser Parser { get; } = parser;
  internal ConsistencyCheck ConsistencyCheck { get; } = consistencyCheck;

  public async ValueTask DisposeAsync() => await WebApp.DisposeAsync();

  public void Run() => WebApp.Run();
  public async Task StartAsync() => await WebApp.StartAsync();
}
