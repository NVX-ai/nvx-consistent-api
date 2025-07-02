global using DeFuncto;
global using DeFuncto.Extensions;
global using static ConsistentAPI.Types;
global using static DeFuncto.Prelude;
global using AuthOptions = DeFuncto.Du4<
  ConsistentAPI.Everyone,
  ConsistentAPI.EveryoneAuthenticated,
  ConsistentAPI.PermissionsRequireAll,
  ConsistentAPI.PermissionsRequireOne>;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ConsistentApi.Tests")]
[assembly: InternalsVisibleTo("TestUtils")]
