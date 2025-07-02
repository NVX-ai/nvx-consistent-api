namespace Nvx.ConsistentAPI.Tests;

public class StrongIdsShould
{
  [Fact(DisplayName = "Render the stream id as expected")]
  public void RenderTheStreamIdAsExpected()
  {
    Assert.Equal("123", $"{new FunnyStrongId("123")}");
    Assert.Equal("banana", $"{new StrongString("banana")}");
  }
}

public record FunnyStrongId(string Value) : StrongId
{
  public override string StreamId() => Value;
  public override string ToString() => StreamId();
}
