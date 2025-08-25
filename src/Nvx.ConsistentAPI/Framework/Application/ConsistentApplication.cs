using Microsoft.AspNetCore.Builder;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI;

public sealed record ConsistentApplication(
  WebApplication WebApplication,
  EventStore<EventModelEvent> Store,
  Fetcher Fetcher)
  : IDisposable, IAsyncDisposable
{
  public async ValueTask DisposeAsync() => await WebApplication.DisposeAsync();

  public void Dispose() => ((IDisposable)WebApplication).Dispose();
  public async Task StartAsync() => await WebApplication.StartAsync();
  public void Run() => WebApplication.Run();
}
