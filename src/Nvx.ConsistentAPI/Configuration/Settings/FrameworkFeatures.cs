namespace Nvx.ConsistentAPI.Configuration.Settings;

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