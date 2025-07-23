using Nvx.ConsistentAPI.EventStore.Events;

namespace Nvx.ConsistentAPI.Tests;

public class TypeNameToSpinalCase
{
  [Theory(DisplayName = "Converts type names as expected")]
  [InlineData(typeof(RegularType), "regular-type")]
  [InlineData(typeof(TypeWithABunchOfCapitalsAndTLA), "type-with-a-bunch-of-capitals-and-tla")]
  [InlineData(typeof(AnEntityReadModel), "an-entity")]
  public void Test1(Type type, string expectation) =>
    Assert.Equal(expectation, Naming.ToSpinalCase(type));

  private record RegularType;

  private record TypeWithABunchOfCapitalsAndTLA;

  private record AnEntityReadModel;
}
