global using DeFuncto;
global using DeFuncto.Extensions;
global using static Nvx.ConsistentAPI.Types;
global using static DeFuncto.Prelude;
global using AuthOptions = DeFuncto.Du4<
  Nvx.ConsistentAPI.Everyone,
  Nvx.ConsistentAPI.EveryoneAuthenticated,
  Nvx.ConsistentAPI.PermissionsRequireAll,
  Nvx.ConsistentAPI.PermissionsRequireOne>;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ConsistentApi.Tests")]
[assembly: InternalsVisibleTo("Nvx.ConsistentAPI.TestUtils")]
