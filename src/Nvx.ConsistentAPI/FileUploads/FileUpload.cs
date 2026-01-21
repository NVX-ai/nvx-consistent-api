namespace Nvx.ConsistentAPI.FileUploads;

public partial record FileUpload(Guid Id, string FileName, string State, string[] Tags) :
  EventModelEntity<FileUpload>,
  Folds<FileUploaded, FileUpload>,
  Folds<FileConfirmed, FileUpload>,
  Folds<FileTagged, FileUpload>
{
  public const string StreamPrefix = "framework-file-upload-";
  public string GetStreamName() => GetStreamName(Id.ToString());

  public ValueTask<FileUpload> Fold(FileConfirmed evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { State = "confirmed" });

  public ValueTask<FileUpload> Fold(FileTagged evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Tags = evt.Tags });

  public ValueTask<FileUpload> Fold(FileUploaded evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { FileName = evt.FileName });

  public static string GetStreamName(string id) => $"{StreamPrefix}{id}";
  public static FileUpload Defaulted(StrongGuid id) => new(id.Value, string.Empty, "pending", []);

  public static EventModel Get(GeneratorSettings settings) =>
    new()
    {
      Entities =
        [new EntityDefinition<FileUpload, StrongGuid> { Defaulter = Defaulted, StreamPrefix = StreamPrefix }],
      Tasks = [FileDefinitions.CleanupUnconfirmed(settings)]
    };
}
