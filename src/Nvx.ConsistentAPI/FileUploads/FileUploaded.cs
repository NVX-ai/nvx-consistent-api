namespace Nvx.ConsistentAPI.FileUploads;

public record FileUploaded(Guid Id, string FileName) : EventModelEvent
{
  public string GetStreamName() => FileUpload.GetStreamName(Id.ToString());
  public StrongId GetEntityId() => new StrongGuid(Id);
}
