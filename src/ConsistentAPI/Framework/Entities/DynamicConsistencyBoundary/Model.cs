namespace ConsistentAPI;

public static class DynamicConsistencyBoundaryModel
{
  public static readonly EventModel Get = new()
  {
    Entities = [ConcernedEntityEntity.Definition, InterestedEntityEntity.Definition]
  };
}
