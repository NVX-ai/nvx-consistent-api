// ReSharper disable NotAccessedPositionalProperty.Local

namespace Nvx.ConsistentAPI.Tests;

public class TypeValidation
{
  [Fact(DisplayName = "Handles nullable properties")]
  public void Test1()
  {
    var validationResult = GetNullabilityViolations(new MyRecord(null, null!, null!));
    Assert.Equal(2, validationResult.Length);
    Assert.Contains("B was null", validationResult);
    Assert.Contains("C was null", validationResult);
  }

  private record MyRecord(string? A, string B, string C);
}
