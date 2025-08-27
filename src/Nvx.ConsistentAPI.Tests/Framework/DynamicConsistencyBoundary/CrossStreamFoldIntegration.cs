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

    var beforeTags = await setup.ReadModel<EntityThatDependsReadModel>(entityThatDependsId.ToString());
    Assert.Empty(beforeTags.DependedOnTags);

    await setup.InsertEvents(new EntityDependedOnTagged(entityDependedOnId, tag));

    var withTags = await setup.ReadModel<EntityThatDependsReadModel>(
      entityThatDependsId.ToString());
    Assert.Single(withTags.DependedOnTags);
    Assert.Contains(withTags.DependedOnTags, t => t == tag);

    await setup.InsertEvents(new EntityDependedOnTagged(entityDependedOnId, Guid.NewGuid().ToString()));
    await setup.InsertEvents(new EntityDependedOnTagged(entityDependedOnId, Guid.NewGuid().ToString()));
    await setup.InsertEvents(new EntityDependedOnTagged(entityDependedOnId, Guid.NewGuid().ToString()));

    var moreTags = await setup.ReadModel<EntityThatDependsReadModel>(
      entityThatDependsId.ToString());
    Assert.Equal(4, moreTags.DependedOnTags.Length);
    Assert.Contains(moreTags.DependedOnTags, t => t == tag);

    await setup.InsertEvents(new EntityThatDependsOnRemovedDependency(entityThatDependsId, entityDependedOnId));
    var afterDependencyRemoved = await setup.ReadModel<EntityThatDependsReadModel>(
      entityThatDependsId.ToString());
    Assert.Empty(afterDependencyRemoved.DependsOnIds);
    Assert.Empty(afterDependencyRemoved.DependedOnTags);

    await setup.InsertEvents(new EntityDependedOnTagged(entityDependedOnId, Guid.NewGuid().ToString()));

    var updatedTagsAfterDependencyRemoved =
      await setup.ReadModel<EntityThatDependsReadModel>(
        entityThatDependsId.ToString());
    Assert.Empty(updatedTagsAfterDependencyRemoved.DependsOnIds);
    Assert.Empty(updatedTagsAfterDependencyRemoved.DependedOnTags);
  }

  [Fact(DisplayName = "should hydrate the read model even on an empty stream")]
  public async Task Test1()
  {
    await using var setup = await Initializer.Do();
    var entityThatDependsId = Guid.NewGuid();
    var entityDependedOnId = Guid.NewGuid();
    await setup.InsertEvents(new EntityDependedOnHeardAboutEntityThatDepends(entityDependedOnId, entityThatDependsId));

    var readModel = await setup.ReadModel<EntityThatDependsReadModel>(
      entityThatDependsId.ToString());
    Assert.Empty(readModel.DependedOnTags);
    Assert.Contains(readModel.DependsOnIds, t => t == entityDependedOnId);
  }

  [Fact(DisplayName = "should get second degree consistency boundary information")]
  public async Task Test2()
  {
    await using var setup = await Initializer.Do();
    var entityThatDependsId = Guid.NewGuid();
    var entityDependedOnId = Guid.NewGuid();
    var furtherId = Guid.NewGuid();
    var farAwayName = Guid.NewGuid().ToString();
    await setup.InsertEvents(new EntityDependedOnEntityDependedOnCreated(furtherId, farAwayName));
    await setup.InsertEvents(new EntityDependedOnStartedDependingOn(entityDependedOnId, furtherId));
    await setup.InsertEvents(new EntityThatDependsOnReceivedDependency(entityThatDependsId, entityDependedOnId));

    var readModel = await setup.ReadModel<EntityThatDependsReadModel>(entityThatDependsId.ToString());

    Assert.Contains(farAwayName, readModel.FarAwayNames);
  }
}
