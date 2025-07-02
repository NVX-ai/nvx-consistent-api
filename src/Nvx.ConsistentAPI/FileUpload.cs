using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Nvx.ConsistentAPI;

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

public record FileUploaded(Guid Id, string FileName) : EventModelEvent
{
  public string GetStreamName() => FileUpload.GetStreamName(Id.ToString());
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record FileConfirmed(Guid Id) : EventModelEvent
{
  public string GetStreamName() => FileUpload.GetStreamName(Id.ToString());
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record FileTagged(Guid Id, string[] Tags) : EventModelEvent
{
  public string GetStreamName() => FileUpload.GetStreamName(Id.ToString());
  public StrongId GetEntityId() => new StrongGuid(Id);
}

public record AttachedFile(Guid Id, string[]? Tags);

public record TryDeleteUnconfirmedFile(Guid Id) : TodoData;

public record FileName(string Name, string[] Tags);

public static class FileDefinitions
{
  public static TodoTaskDefinition CleanupUnconfirmed(GeneratorSettings settings) =>
    new TodoTaskDefinition<TryDeleteUnconfirmedFile, FileUpload, FileUploaded, StrongGuid>
    {
      Type = "framework-cleanup-unconfirmed-files",
      Action = async (file, upload, _, _, _) =>
      {
        if (upload.State != "pending")
        {
          return TodoOutcome.Done;
        }

        var blobClient = new BlobClient(
          settings.BlobStorageConnectionString,
          "framework-storage",
          upload.Id.ToString());
        await blobClient.DeleteIfExistsAsync();
        return new AnyState(new FileConfirmed(file.Id));
      },
      Originator = (evt, _, _) => new TryDeleteUnconfirmedFile(evt.Id),
      SourcePrefix = FileUpload.StreamPrefix,
      Delay = TimeSpan.FromHours(6)
    };

  public static async Task InitializeEndpoint(
    WebApplication app,
    Emitter emitter,
    Fetcher fetcher,
    GeneratorSettings settings)
  {
    var client = new BlobContainerClient(settings.BlobStorageConnectionString, "framework-storage");

    await client.CreateIfNotExistsAsync();
    Delegate handler =
      async (HttpContext context, IFormFile file) =>
      {
        var entityId = Guid.NewGuid();
        var entity = await fetcher.Fetch<FileUpload>(new StrongGuid(entityId));
        if (entity.Ent.IsSome)
        {
          await new ConflictError("Tried to upload the same file twice.").Respond(context);
        }

        await client.UploadBlobAsync(entityId.ToString(), file.OpenReadStream());
        await emitter
          .Emit(() => new CreateStream(new FileUploaded(entityId, file.FileName)))
          .Async()
          .Map(id => new CommandAcceptedResult(id))
          .Apply(Respond(context));
      };

    if (settings.EnabledFeatures.HasCommands())
    {
      app
        .MapPost("/files/upload", handler)
        // For some reason, Microsoft decided that endpoints with `IFormFile` and `IFormFileCollection`
        // have to use anti forgery by default.
        .DisableAntiforgery()
        .WithTags(OperationTags.Files);
    }

    Delegate downloadHandler =
      async (Guid id) =>
      {
        var entity = await fetcher.Fetch<FileUpload>(new StrongGuid(id));
        return
          await entity.Ent.Match<Task<IResult>>(
            async e =>
            {
              var blobClient = new BlobClient(
                settings.BlobStorageConnectionString,
                "framework-storage",
                e.Id.ToString());
              var file = await blobClient.DownloadStreamingAsync();
              return TypedResults.File(file.Value.Content, fileDownloadName: e.FileName);
            },
            () => (TypedResults.NotFound() as IResult).ToTask()
          );
      };

    Delegate fileNameHandler =
      async (Guid id) =>
      {
        var entity = await fetcher.Fetch<FileUpload>(new StrongGuid(id));
        return
          await entity.Ent.Match<Task<IResult>>(
            e => Task.FromResult<IResult>(TypedResults.Ok(new FileName(e.FileName, e.Tags))),
            () => (TypedResults.NotFound() as IResult).ToTask()
          );
      };

    if (!settings.EnabledFeatures.HasQueries())
    {
      return;
    }

    app.MapGet("/files/download/{id:Guid}", downloadHandler).WithTags(OperationTags.Files);
    app.MapGet("/files/name/{id:Guid}", fileNameHandler).Produces<FileName>().WithTags(OperationTags.Files);

    return;

    Func<AsyncResult<CommandAcceptedResult, ApiError>, Task> Respond(HttpContext context) =>
      r => r.Match(
        async car =>
        {
          context.Response.StatusCode = StatusCodes.Status200OK;
          await context.Response.WriteAsJsonAsync(car);
        },
        async err => await err.Respond(context)
      );
  }
}
