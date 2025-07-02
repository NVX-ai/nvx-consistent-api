namespace ConsistentAPI.Tests.Framework;

public class Progress
{
  [Theory(DisplayName = "should calculate progress backwards")]
  [InlineData(0, 10, 100)]
  [InlineData(0, 0, 100)]
  [InlineData(5, 10, 50)]
  [InlineData(75, 100, 25)]
  [InlineData(10, 100, 90)]
  [InlineData(20, 200, 90)]
  public void Test(decimal current, decimal last, decimal expected) => Assert.Equal(
    expected,
    ReadModelProgress.InventerPercentageProgress(current, last));
}
