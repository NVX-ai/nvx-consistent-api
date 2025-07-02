namespace Nvx.ConsistentAPI.Tests;

public class HashingShould
{
  [Fact(DisplayName = "be consistent")]
  public void Test()
  {
    var model1 = TestModel.GetModel();
    var model2 = TestModel.GetModel();
    Assert.NotEqual(model1, model2);
    Assert.Equal(model1.GetHashCode(), model2.GetHashCode());
    var fileModel1 = EntityWithFiles.Model();
    var fileModel2 = EntityWithFiles.Model();
    Assert.NotEqual(fileModel1, fileModel2);
    Assert.Equal(fileModel1.GetHashCode(), fileModel2.GetHashCode());
  }
}
