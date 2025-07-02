// ReSharper disable NotAccessedPositionalProperty.Local

namespace Nvx.ConsistentAPI.Tests;

public class TableNaming
{
  [Fact(DisplayName = "Names tables consistently")]
  public void Test1()
  {
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeA)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeB))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeA)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeC))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeA)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeD))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeA)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeE))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeA)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeF))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeB)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeC))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeB)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeD))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeB)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeE))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeB)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeF))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeC)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeD))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeC)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeE))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeC)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeF))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeD)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeE))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeD)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeF))
    );
    Assert.NotEqual(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeE)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeF))
    );
    Assert.Equal(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeA)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeA))
    );
    Assert.Equal(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeB)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeB))
    );
    Assert.Equal(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeC)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeC))
    );
    Assert.Equal(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeD)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeD))
    );
    Assert.Equal(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeE)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeE))
    );
    Assert.Equal(
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeF)),
      DatabaseHandler<RegularTypeA>.TableName(typeof(RegularTypeF))
    );
  }

  private record RegularTypeA(string Name, int Age, bool IsSomething, string Id) : EventModelReadModel
  {
    public StrongId GetStrongId() => throw new NotImplementedException();
  }

  private record RegularTypeB(string Name, int? Age, bool IsSomething, string Id) : EventModelReadModel
  {
    public StrongId GetStrongId() => throw new NotImplementedException();
  }

  private record RegularTypeC(string Name, int Age, bool? IsSomething, string Id) : EventModelReadModel
  {
    public StrongId GetStrongId() => throw new NotImplementedException();
  }

  private record RegularTypeD(string Name, int Age, bool IsSomething, string Id) : EventModelReadModel
  {
    public StrongId GetStrongId() => throw new NotImplementedException();
  }

  private record RegularTypeE(string? Name, int Age, bool IsSomething, string Id) : EventModelReadModel
  {
    public StrongId GetStrongId() => throw new NotImplementedException();
  }

  private record RegularTypeF(string Name, int Age, bool IsSomething, string Id) : EventModelReadModel
  {
    public StrongId GetStrongId() => throw new NotImplementedException();
  }
}
