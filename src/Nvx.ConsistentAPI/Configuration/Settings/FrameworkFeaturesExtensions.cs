namespace Nvx.ConsistentAPI.Configuration.Settings;

public static class FrameworkFeaturesExtensions
{
  public static bool HasEndpoints(this FrameworkFeatures features) =>
    features.HasQueries() || features.HasCommands() || features.HasIngestors();

  public static bool HasQueries(this FrameworkFeatures features) =>
    ((FrameworkFeatures.StaticEndpoints
      | FrameworkFeatures.SystemEndpoints
      | FrameworkFeatures.ReadModelEndpoints)
     & features)
    != FrameworkFeatures.None;

  public static bool HasCommands(this FrameworkFeatures features) =>
    (FrameworkFeatures.Commands & features) != FrameworkFeatures.None;

  public static bool HasIngestors(this FrameworkFeatures features) =>
    (FrameworkFeatures.Ingestors & features) != FrameworkFeatures.None;
}
