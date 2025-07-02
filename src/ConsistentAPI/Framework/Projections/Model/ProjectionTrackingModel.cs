namespace ConsistentAPI.Framework.Projections.Model;

public static class ProjectionTrackingModel
{
  public static readonly EventModel Get = new() { Entities = [ProjectionTrackerEntity.Definition] };
}
