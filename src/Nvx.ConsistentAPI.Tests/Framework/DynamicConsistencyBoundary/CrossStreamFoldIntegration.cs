namespace Nvx.ConsistentAPI.Tests.Framework.DynamicConsistencyBoundary;

public class CrossStreamFoldIntegration
{
  [Fact(DisplayName = "should rehydrate the read model on targeted events when an external fold is used")]
  public async Task Test0()
  {
    await using var setup = await Initializer.Do();
    var interestedEntityId = Guid.NewGuid();
    var concernedEntityId = Guid.NewGuid();
    var tag = Guid.NewGuid().ToString();
    await setup.InsertEvents(new InterestedEntityAddedAnInterest(interestedEntityId, concernedEntityId));

    var beforeTags = await setup.ReadModel<EntityThatDependsReadModel>(interestedEntityId.ToString());
    Assert.Empty(beforeTags.DependedOnTags);

    await setup.InsertEvents(new FirstDegreeConcernedEntityTagged(concernedEntityId, tag));

    var withTags = await setup.ReadModel<EntityThatDependsReadModel>(
      interestedEntityId.ToString());
    Assert.Single(withTags.DependedOnTags);
    Assert.Contains(withTags.DependedOnTags, t => t == tag);

    await setup.InsertEvents(new FirstDegreeConcernedEntityTagged(concernedEntityId, Guid.NewGuid().ToString()));
    await setup.InsertEvents(new FirstDegreeConcernedEntityTagged(concernedEntityId, Guid.NewGuid().ToString()));
    await setup.InsertEvents(new FirstDegreeConcernedEntityTagged(concernedEntityId, Guid.NewGuid().ToString()));

    var moreTags = await setup.ReadModel<EntityThatDependsReadModel>(
      interestedEntityId.ToString());
    Assert.Equal(4, moreTags.DependedOnTags.Length);
    Assert.Contains(moreTags.DependedOnTags, t => t == tag);

    await setup.InsertEvents(new InterestedEntityRemovedInterest(interestedEntityId, concernedEntityId));
    var afterDependencyRemoved = await setup.ReadModel<EntityThatDependsReadModel>(
      interestedEntityId.ToString());
    Assert.Empty(afterDependencyRemoved.DependsOnIds);
    Assert.Empty(afterDependencyRemoved.DependedOnTags);

    await setup.InsertEvents(new FirstDegreeConcernedEntityTagged(concernedEntityId, Guid.NewGuid().ToString()));

    var updatedTagsAfterDependencyRemoved =
      await setup.ReadModel<EntityThatDependsReadModel>(
        interestedEntityId.ToString());
    Assert.Empty(updatedTagsAfterDependencyRemoved.DependsOnIds);
    Assert.Empty(updatedTagsAfterDependencyRemoved.DependedOnTags);
  }

  [Fact(DisplayName = "should hydrate the read model even on an empty stream")]
  public async Task Test1()
  {
    await using var setup = await Initializer.Do();
    var interestedEntityId = Guid.NewGuid();
    var concernedEntityId = Guid.NewGuid();
    await setup.InsertEvents(
      new FirstDegreeConcernedEntityEventAboutInterestedEntity(concernedEntityId, interestedEntityId));

    var readModel = await setup.ReadModel<EntityThatDependsReadModel>(
      interestedEntityId.ToString());
    Assert.Empty(readModel.DependedOnTags);
    Assert.Contains(readModel.DependsOnIds, t => t == concernedEntityId);
  }

  [Fact(DisplayName = "should get second degree consistency boundary information")]
  public async Task Test2()
  {
    await using var setup = await Initializer.Do();
    var interestedId = Guid.NewGuid();
    var firstId = Guid.NewGuid();
    var secondId = Guid.NewGuid();
    var secondDegreeName = $"Original name {secondId}";
    await setup.InsertEvents(new SecondDegreeConcernedEntityNamed(secondId, secondDegreeName));
    await setup.InsertEvents(new EntityDependedOnStartedDependingOn(firstId, secondId));
    await setup.InsertEvents(new InterestedEntityAddedAnInterest(interestedId, firstId));

    var readModel = await setup.ReadModel<EntityThatDependsReadModel>(interestedId.ToString());

    Assert.Contains(secondDegreeName, readModel.FarAwayNames);

    var newSecondDegreeName = $"New name {secondId}";
    await setup.InsertEvents(new SecondDegreeConcernedEntityNamed(secondId, newSecondDegreeName));

    var readModelWithTwoNames = await setup.ReadModel<EntityThatDependsReadModel>(interestedId.ToString());
    Assert.Contains(newSecondDegreeName, readModelWithTwoNames.FarAwayNames);
    Assert.Contains(secondDegreeName, readModelWithTwoNames.FarAwayNames);
    Assert.Equal(2, readModelWithTwoNames.FarAwayNames.Length);
  }
}
