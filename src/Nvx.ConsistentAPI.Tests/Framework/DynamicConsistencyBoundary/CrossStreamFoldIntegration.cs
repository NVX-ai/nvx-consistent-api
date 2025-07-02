namespace Nvx.ConsistentAPI.Tests.Framework.DynamicConsistencyBoundary;

public class CrossStreamFoldIntegration
{
  [Fact(DisplayName = "should rehydrate the read model on targeted events when an external fold is used")]
  public async Task Test0()
  {
    await using var setup = await Initializer.Do();
    var entityThatDependsId = Guid.NewGuid();
    var entityDependedOnId = Guid.NewGuid();
    var tag = Guid.NewGuid().ToString();
    await setup.InsertEvents(new EntityThatDependsOnReceivedDependency(entityThatDependsId, entityDependedOnId));
    await EventuallyConsistent.WaitFor(async () =>
    {
      var readModel = await setup.ReadModel<EntityThatDependsReadModel>(entityThatDependsId.ToString());
      Assert.Empty(readModel.DependedOnTags);
    });
    await setup.InsertEvents(new EntityDependedOnTagged(entityDependedOnId, tag));
    await EventuallyConsistent.WaitFor(async () =>
    {
      var readModel = await setup.ReadModel<EntityThatDependsReadModel>(entityThatDependsId.ToString());
      Assert.Single(readModel.DependedOnTags);
      Assert.Contains(readModel.DependedOnTags, t => t == tag);
    });

    await setup.InsertEvents(new EntityDependedOnTagged(entityDependedOnId, Guid.NewGuid().ToString()));
    await setup.InsertEvents(new EntityDependedOnTagged(entityDependedOnId, Guid.NewGuid().ToString()));
    await setup.InsertEvents(new EntityDependedOnTagged(entityDependedOnId, Guid.NewGuid().ToString()));

    await EventuallyConsistent.WaitFor(async () =>
    {
      var readModel = await setup.ReadModel<EntityThatDependsReadModel>(entityThatDependsId.ToString());
      Assert.Equal(4, readModel.DependedOnTags.Length);
      Assert.Contains(readModel.DependedOnTags, t => t == tag);
    });

    await setup.InsertEvents(new EntityThatDependsOnRemovedDependency(entityThatDependsId, entityDependedOnId));
    await EventuallyConsistent.WaitFor(async () =>
    {
      var readModel = await setup.ReadModel<EntityThatDependsReadModel>(entityThatDependsId.ToString());
      Assert.Empty(readModel.DependsOnIds);
      Assert.Empty(readModel.DependedOnTags);
    });

    await setup.InsertEvents(new EntityDependedOnTagged(entityDependedOnId, Guid.NewGuid().ToString()));

    await EventuallyConsistent.WaitForAggregation(async () =>
    {
      var readModel = await setup.ReadModel<EntityThatDependsReadModel>(entityThatDependsId.ToString());
      Assert.Empty(readModel.DependsOnIds);
      Assert.Empty(readModel.DependedOnTags);
    });
  }

  [Fact(DisplayName = "should hydrate the read model even on an empty stream")]
  public async Task Test1()
  {
    await using var setup = await Initializer.Do();
    var entityThatDependsId = Guid.NewGuid();
    var entityDependedOnId = Guid.NewGuid();
    await setup.InsertEvents(new EntityDependedOnHeardAboutEntityThatDepends(entityDependedOnId, entityThatDependsId));
    await EventuallyConsistent.WaitFor(async () =>
    {
      var readModel = await setup.ReadModel<EntityThatDependsReadModel>(entityThatDependsId.ToString());
      Assert.Empty(readModel.DependedOnTags);
      Assert.Contains(readModel.DependsOnIds, t => t == entityDependedOnId);
    });
  }
}
