namespace Nvx.ConsistentAPI.Tests;

public class AsyncFoldsIntegration
{
  [Fact(DisplayName = "folds getting another entity in the revision of the event being folded")]
  public async Task Test()
  {
    await using var setup = await Initializer.Do();
    var parentId = await setup.Command(new CreatePerson("Parent")).Map(car => car.EntityId).Map(Guid.Parse);
    var child1Id = await setup.Command(new CreatePerson("Child 1")).Map(car => car.EntityId).Map(Guid.Parse);
    var child2Id = await setup.Command(new CreatePerson("Child 2")).Map(car => car.EntityId).Map(Guid.Parse);
    await setup.Command(new AddChild(parentId, child1Id));
    await setup.Command(new RenamePerson(child1Id, "Renamed Child 1"));
    await setup.Command(new AddChild(parentId, child2Id));
    await EventuallyConsistent.WaitFor(async () =>
    {
      var parentReadModel = await setup.ReadModel<PersonReadModel>(parentId.ToString());
      var child1ReadModel = await setup.ReadModel<PersonReadModel>(child1Id.ToString());
      Assert.Equal("Child 1", parentReadModel.Children.Single(c => c.PersonId == child1Id).Name);
      Assert.Equal("Renamed Child 1", child1ReadModel.Name);
    });
    await setup.Command(new AddChild(parentId, child1Id));
    await EventuallyConsistent.WaitFor(async () =>
    {
      var parentReadModel = await setup.ReadModel<PersonReadModel>(parentId.ToString());
      Assert.Equal("Renamed Child 1", parentReadModel.Children.Single(c => c.PersonId == child1Id).Name);
    });
  }
}
