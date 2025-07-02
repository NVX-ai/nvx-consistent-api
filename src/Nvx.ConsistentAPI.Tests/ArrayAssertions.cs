namespace Nvx.ConsistentAPI.Tests;

public static class ArrayAssertions
{
  public static void ShouldBeSingle<T>(this T[] array, Action<T> assertion)
  {
    Assert.Single(array);
    assertion(array[0]);
  }
}
