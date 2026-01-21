using Serilog;

namespace Nvx.ConsistentAPI.Logging;

internal static class LogFileRollIntervalExtensions
{
  internal static RollingInterval ToSerilog(this LogFileRollInterval interval) =>
    interval switch
    {
      LogFileRollInterval.Year => RollingInterval.Year,
      LogFileRollInterval.Month => RollingInterval.Month,
      LogFileRollInterval.Day => RollingInterval.Day,
      LogFileRollInterval.Hour => RollingInterval.Hour,
      LogFileRollInterval.Minute => RollingInterval.Minute,
      _ => RollingInterval.Infinite
    };
}
