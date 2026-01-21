global using GeneratorSettings = Nvx.ConsistentAPI.Configuration.Settings.GeneratorSettings;
global using FrameworkFeatures = Nvx.ConsistentAPI.Configuration.Settings.FrameworkFeatures;
global using ILogger = Microsoft.Extensions.Logging.ILogger;
global using DeFuncto;
global using DeFuncto.Extensions;
global using static Nvx.ConsistentAPI.Types;
global using static DeFuncto.Prelude;
global using AuthOptions = DeFuncto.Du4<
  Nvx.ConsistentAPI.Security.Everyone,
  Nvx.ConsistentAPI.Security.EveryoneAuthenticated,
  Nvx.ConsistentAPI.Security.PermissionsRequireAll,
  Nvx.ConsistentAPI.Security.PermissionsRequireOne>;
global using Nvx.ConsistentAPI.EventModeling;
global using Nvx.ConsistentAPI.FileUploads;
global using Nvx.ConsistentAPI.RecurringTasks;
global using Nvx.ConsistentAPI.Idempotency;
global using Nvx.ConsistentAPI.Errors;
global using Nvx.ConsistentAPI.Logging;
global using Nvx.ConsistentAPI.SignalR;
global using Nvx.ConsistentAPI.Security;
global using Nvx.ConsistentAPI.ValidationRules;
global using Nvx.ConsistentAPI.Framework.Commands;
global using Nvx.ConsistentAPI.Framework.Entities;
global using Nvx.ConsistentAPI.Framework.Events;
global using Nvx.ConsistentAPI.Framework.Serialization;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Nvx.ConsistentApi.Tests")]
[assembly: InternalsVisibleTo("Nvx.ConsistentAPI.TestUtils")]
