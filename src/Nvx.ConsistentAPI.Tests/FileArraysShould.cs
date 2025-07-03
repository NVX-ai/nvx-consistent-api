namespace Nvx.ConsistentAPI.Tests;

public class FileArraysShould
{
  [Fact(DisplayName = "get the filename and tags as expected")]
  public async Task GetTheFilenameAndTagsAsExpected()
  {
    await using var setup = await Initializer.Do();
    await setup.ResetReadModel<EntityWithFilesReadModel>();
    var file1Id = await setup.Upload().Map(car => car.EntityId).Map(Guid.Parse);
    var file2Id = await setup.Upload().Map(car => car.EntityId).Map(Guid.Parse);
    var file3Id = await setup.UploadPath("customtextfile.txt").Map(car => car.EntityId).Map(Guid.Parse);
    var entityId = Guid.NewGuid().ToString();
    await setup.Command(
      new SaveEntityWithFiles(
        entityId,
        [
          new AttachedFile(file1Id, ["one", "two"]),
          new AttachedFile(file2Id, ["three", "four"]),
          new AttachedFile(file3Id, ["five", "six"])
        ]));

    var readModel = await setup.ReadModel<EntityWithFilesReadModel>(entityId);
    Assert.Equal(2, readModel.Files.Count(f => f.FileName == "text.txt"));
    Assert.Single(readModel.Files, f => f.FileName == "customtextfile.txt");
    Assert.True(readModel.Files.All(f => f.Tags.Length == 2));
    Assert.Contains(readModel.Files, f => f.Tags.Contains("one") && f.Tags.Contains("two"));
    Assert.Contains(readModel.Files, f => f.Tags.Contains("three") && f.Tags.Contains("four"));
    Assert.Contains(readModel.Files, f => f.Tags.Contains("five") && f.Tags.Contains("six"));
    await setup.DownloadAndComparePath(file3Id, "customtextfile.txt");
  }
}
