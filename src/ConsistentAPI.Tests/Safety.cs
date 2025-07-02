namespace ConsistentAPI.Tests;

public class Safety
{
  [Fact(DisplayName = "Safety for events")]
  public void SafetyForEvents() => Generator.ValidateEventCohesion();
}
