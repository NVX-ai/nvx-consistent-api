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

    var beforeTags = await setup.ReadModel<EntityThatDependsReadModel>(
      interestedEntityId.ToString(),
      waitType: ConsistencyWaitType.Long);
    Assert.Empty(beforeTags.DependedOnTags);

    await setup.InsertEvents(new FirstDegreeConcernedEntityTagged(concernedEntityId, tag));

    var withTags = await setup.ReadModel<EntityThatDependsReadModel>(
      interestedEntityId.ToString(),
      waitType: ConsistencyWaitType.Long);
    Assert.Single(withTags.DependedOnTags);
    Assert.Contains(withTags.DependedOnTags, t => t == tag);

    await setup.InsertEvents(new FirstDegreeConcernedEntityTagged(concernedEntityId, Guid.NewGuid().ToString()));
    await setup.InsertEvents(new FirstDegreeConcernedEntityTagged(concernedEntityId, Guid.NewGuid().ToString()));
    await setup.InsertEvents(new FirstDegreeConcernedEntityTagged(concernedEntityId, Guid.NewGuid().ToString()));

    var moreTags = await setup.ReadModel<EntityThatDependsReadModel>(
      interestedEntityId.ToString(),
      waitType: ConsistencyWaitType.Long);
    Assert.Equal(4, moreTags.DependedOnTags.Length);
    Assert.Contains(moreTags.DependedOnTags, t => t == tag);

    await setup.InsertEvents(new InterestedEntityRemovedInterest(interestedEntityId, concernedEntityId));
    var afterDependencyRemoved = await setup.ReadModel<EntityThatDependsReadModel>(
      interestedEntityId.ToString(),
      waitType: ConsistencyWaitType.Long);
    Assert.Empty(afterDependencyRemoved.DependsOnIds);
    Assert.Empty(afterDependencyRemoved.DependedOnTags);

    await setup.InsertEvents(new FirstDegreeConcernedEntityTagged(concernedEntityId, Guid.NewGuid().ToString()));

    var updatedTagsAfterDependencyRemoved =
      await setup.ReadModel<EntityThatDependsReadModel>(
        interestedEntityId.ToString(),
        waitType: ConsistencyWaitType.Long);
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
    await setup.WaitFor<InterestedEntityRegisteredInterest>(
      e => e.InterestedEntityStreamName == EntityThatIsInterested.GetStreamName(interestedEntityId),
      InterestedEntityEntity.GetStreamName(
        new InterestedEntityId(EntityThatIsInterested.GetStreamName(interestedEntityId))));

    var readModel = await setup.ReadModelWhen<EntityThatIsInterested, EntityThatDependsReadModel>(
      new StrongGuid(interestedEntityId),
      rm => rm.DependsOnIds.Contains(concernedEntityId));
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

    await setup.InsertEvents(new FirstDegreeStartedDependingOnSecondDegree(firstId, secondId));
    await setup.WaitFor<InterestedEntityRegisteredInterest>(e =>
      e.InterestedEntityStreamName == FirstDegreeConcernedEntity.GetStreamName(firstId));

    await setup.InsertEvents(new InterestedEntityAddedAnInterest(interestedId, firstId));
    await setup.WaitFor<InterestedEntityRegisteredInterest>(
      e => e.InterestedEntityStreamName == EntityThatIsInterested.GetStreamName(interestedId),
      InterestedEntityEntity.GetStreamName(new InterestedEntityId(EntityThatIsInterested.GetStreamName(interestedId))));

    var readModel = await setup.ReadModel<EntityThatDependsReadModel>(
      interestedId.ToString());

    Assert.Contains(secondDegreeName, readModel.FarAwayNames);

    var newSecondDegreeName = $"New name {secondId}";
    await setup.InsertEvents(new SecondDegreeConcernedEntityNamed(secondId, newSecondDegreeName));

    var readModelWithTwoNames = await setup.ReadModelWhen<EntityThatIsInterested, EntityThatDependsReadModel>(
      new StrongGuid(interestedId),
      rm => rm.FarAwayNames.Contains(newSecondDegreeName));

    Assert.Contains(newSecondDegreeName, readModelWithTwoNames.FarAwayNames);
    Assert.Contains(secondDegreeName, readModelWithTwoNames.FarAwayNames);
    Assert.Equal(2, readModelWithTwoNames.FarAwayNames.Length);
  }
}
