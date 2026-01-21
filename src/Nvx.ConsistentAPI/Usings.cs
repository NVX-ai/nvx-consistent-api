global using GeneratorSettings = Nvx.ConsistentAPI.Configuration.Settings.GeneratorSettings;
global using FrameworkFeatures = Nvx.ConsistentAPI.Configuration.Settings.FrameworkFeatures;
global using ILogger = Microsoft.Extensions.Logging.ILogger;
global using DeFuncto;
global using DeFuncto.Extensions;
global using static Nvx.ConsistentAPI.Types;
global using static DeFuncto.Prelude;
global using AuthOptions = DeFuncto.Du4<
  Nvx.ConsistentAPI.Everyone,
  Nvx.ConsistentAPI.EveryoneAuthenticated,
  Nvx.ConsistentAPI.PermissionsRequireAll,
  Nvx.ConsistentAPI.PermissionsRequireOne>;
global using Nvx.ConsistentAPI.EventModeling;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Nvx.ConsistentApi.Tests")]
[assembly: InternalsVisibleTo("Nvx.ConsistentAPI.TestUtils")]
