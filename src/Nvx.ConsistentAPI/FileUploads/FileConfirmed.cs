namespace Nvx.ConsistentAPI.FileUploads;

public record FileConfirmed(Guid Id) : EventModelEvent
{
  public string GetStreamName() => FileUpload.GetStreamName(Id.ToString());
  public StrongId GetEntityId() => new StrongGuid(Id);
}
