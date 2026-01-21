namespace Nvx.ConsistentAPI.FileUploads;

public record FileTagged(Guid Id, string[] Tags) : EventModelEvent
{
  public string GetStreamName() => FileUpload.GetStreamName(Id.ToString());
  public StrongId GetEntityId() => new StrongGuid(Id);
}
