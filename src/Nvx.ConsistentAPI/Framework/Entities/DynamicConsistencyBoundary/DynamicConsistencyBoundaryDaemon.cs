using EventStore.Client;
using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Framework;
using Nvx.ConsistentAPI.InternalTooling;

namespace Nvx.ConsistentAPI;

public class DynamicConsistencyBoundaryDaemon(
  EventStoreClient client,
  Func<ResolvedEvent, Option<EventModelEvent>> parser,
  InterestTrigger[] triggers,
  ILogger logger,
  InterestFetcher interestFetcher)
{
  private readonly SemaphoreSlim semaphore = new(25);
  private ulong? currentProcessedPosition;
  private ulong? currentSweepPosition;
  private int interestsRegisteredSinceStartup;
  private int interestsRemovedSinceStartup;

  private bool isSweepCompleted;

  public DynamicConsistencyBoundaryDaemonInsights Insights(ulong lastEventPositon)
  {
    return new DynamicConsistencyBoundaryDaemonInsights(
      ProcessedPosition(),
      CurrentPercentageComplete(),
      currentSweepPosition,
      isSweepCompleted,
      interestsRegisteredSinceStartup,
      interestsRemovedSinceStartup,
      SweepPercentageComplete());

    ulong ProcessedPosition() => currentProcessedPosition ?? lastEventPositon;

    decimal CurrentPercentageComplete() => Math.Min(
      100,
      lastEventPositon == 0
        ? 100m
        : 100m * Convert.ToDecimal(currentProcessedPosition ?? 0) / Convert.ToDecimal(lastEventPositon));

    decimal SweepPercentageComplete() =>
      Math.Min(
        100,
        lastEventPositon == 0 || isSweepCompleted
          ? 100m
          : 100m * Convert.ToDecimal(currentSweepPosition ?? 0) / Convert.ToDecimal(lastEventPositon));
  }

  internal void Initialize()
  {
    _ = Task.Run(async () =>
    {
      var position = FromAll.End;
      while (true)
      {
        try
        {
          await foreach (var msg in client.SubscribeToAll(
                             position,
                             filterOptions: new SubscriptionFilterOptions(EventTypeFilter.ExcludeSystemEvents()))
                           .Messages)
          {
            switch (msg)
            {
              case StreamMessage.Event evt:
                var re = evt.ResolvedEvent;
                foreach (var parsed in parser(re))
                {
                  await TriggerInterests(parsed, re.Event.EventId);
                }

                position = FromAll.After(re.Event.Position);
                currentProcessedPosition = re.Event.Position.CommitPosition;
                break;
            }
          }
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Error in the catch-up scan for dynamic consistency boundary");
          await Task.Delay(250);
        }
      }
      // ReSharper disable once FunctionNeverReturns
    });
    _ = Task.Run(async () =>
    {
      var shouldKeepRunning = true;
      var position = Position.Start;
      var tasks = new List<Task>();
      while (shouldKeepRunning)
      {
        try
        {
          await foreach (var re in client.ReadAllAsync(
                           Direction.Forwards,
                           position,
                           EventTypeFilter.ExcludeSystemEvents()))
          {
            tasks.AddRange(
              parser(re)
                .Select(parsed => Task.Run(async () =>
                {
                  try
                  {
                    await semaphore.WaitAsync();
                    await TriggerInterests(parsed, re.Event.EventId);
                  }
                  finally
                  {
                    semaphore.Release();
                  }
                })));

            if (tasks.Count > 10)
            {
              await Task.WhenAll(tasks);
              tasks.Clear();
            }

            // ReSharper disable once RedundantAssignment
            position = re.Event.Position;
            currentSweepPosition = position.CommitPosition;
          }

          shouldKeepRunning = false;
        }
        catch (Exception ex)
        {
          logger.LogError(ex, "Error in the failsafe scan for dynamic consistency boundary");
          await Task.Delay(1_000);
        }
      }

      isSweepCompleted = true;
    });
  }

  private async Task TriggerInterests(EventModelEvent evt, Uuid originatingEventId)
  {
    var stops = triggers.SelectMany(t => t.Stops(evt)).Distinct().ToArray();
    var initiates = triggers.SelectMany(t => t.Initiates(evt)).Distinct().ToArray();

    var interestedEntities = await stops
      .Concat(initiates)
      .Select(i => i.InterestedEntityStreamName)
      .Distinct()
      .Select<string, Func<Task<Option<InterestedEntityEntity>>>>(streamName =>
        async () => await interestFetcher.Interested(streamName))
      .Parallel()
      .Map(entities => entities.Choose().ToArray());

    await Task.WhenAll(
      RemoveInterests(
        FilterOutIrrelevantStops(stops, interestedEntities, originatingEventId.ToString()),
        originatingEventId),
      RegisterNewInterests(
        FilterOutIrrelevantStarts(initiates, interestedEntities, originatingEventId.ToString()),
        originatingEventId)
    );
  }

  private static EntityInterestManifest[] FilterOutIrrelevantStops(
    EntityInterestManifest[] interests,
    InterestedEntityEntity[] entities,
    string originatingEventId) =>
    interests
      .Select(manifest => (manifest,
        entity: entities.FirstOrNone(ie => ie.InterestedEntityStreamName == manifest.InterestedEntityStreamName)))
      .Where(WillStopExistingInterest(originatingEventId))
      .Select(t => t.manifest)
      .ToArray();

  private static Func<(EntityInterestManifest manifest, Option<InterestedEntityEntity> entity), bool>
    WillStopExistingInterest(string originatingEventId) =>
    t => t.entity.Match(
      e =>
        !e.OriginatingEventIds.Contains(originatingEventId)
        && e.ConcernedStreams.Select(cs => cs.name).Contains(t.manifest.ConcernedEntityStreamName),
      () => false);

  private static EntityInterestManifest[] FilterOutIrrelevantStarts(
    EntityInterestManifest[] interests,
    InterestedEntityEntity[] entities,
    string originatingEventId) =>
    interests
      .Select(manifest =>
        (manifest,
          entity: entities.FirstOrNone(ie => ie.InterestedEntityStreamName == manifest.InterestedEntityStreamName)))
      .Where(WillInitiateNewInterest(originatingEventId))
      .Select(t => t.manifest)
      .ToArray();

  private static Func<(EntityInterestManifest manifest, Option<InterestedEntityEntity> entity), bool>
    WillInitiateNewInterest(string originatingEventId) =>
    t => t.entity.Match(
      e =>
        !e.OriginatingEventIds.Contains(originatingEventId)
        && !e.ConcernedStreams.Select(cs => cs.name).Contains(t.manifest.ConcernedEntityStreamName),
      () => true);

  private async Task RegisterNewInterests(EntityInterestManifest[] interests, Uuid originatingEventId) =>
    await interests
      .Select<EntityInterestManifest, Func<Task<Unit>>>(interestedStream =>
        async () =>
        {
          await Task.WhenAll(
            InsertEvent(
              new ConcernedEntityReceivedInterest(
                interestedStream.ConcernedEntityStreamName,
                ToDictionary(interestedStream.ConcernedEntityId),
                interestedStream.InterestedEntityStreamName,
                ToDictionary(interestedStream.InterestedEntityId),
                originatingEventId.ToString())),
            InsertEvent(
              new InterestedEntityRegisteredInterest(
                interestedStream.InterestedEntityStreamName,
                ToDictionary(interestedStream.InterestedEntityId),
                interestedStream.ConcernedEntityStreamName,
                ToDictionary(interestedStream.ConcernedEntityId),
                originatingEventId.ToString())));
          return unit;
        })
      .Parallel();

  private async Task RemoveInterests(EntityInterestManifest[] interests, Uuid originatingEventId) =>
    await interests
      .Select<EntityInterestManifest, Func<Task<Unit>>>(interestedStream =>
        async () =>
        {
          await Task.WhenAll(
            InsertEvent(
              new ConcernedEntityHadInterestRemoved(
                interestedStream.ConcernedEntityStreamName,
                ToDictionary(interestedStream.ConcernedEntityId),
                interestedStream.InterestedEntityStreamName,
                ToDictionary(interestedStream.InterestedEntityId),
                originatingEventId.ToString())),
            InsertEvent(
              new InterestedEntityHadInterestRemoved(
                interestedStream.InterestedEntityStreamName,
                ToDictionary(interestedStream.InterestedEntityId),
                interestedStream.ConcernedEntityStreamName,
                ToDictionary(interestedStream.ConcernedEntityId),
                originatingEventId.ToString())));
          return unit;
        })
      .Parallel();

  private async Task InsertEvent(EventModelEvent evt)
  {
    await client.AppendToStreamAsync(
      evt.GetStreamName(),
      StreamState.Any,
      [
        new EventData(
          Uuid.NewUuid(),
          evt.EventType,
          evt.ToBytes(),
          new EventMetadata(
              DateTime.UtcNow,
              null,
              null,
              null,
              null)
            .ToBytes())
      ]);

    switch (evt)
    {
      case InterestedEntityRegisteredInterest:
        Interlocked.Increment(ref interestsRegisteredSinceStartup);
        break;
      case InterestedEntityHadInterestRemoved:
        Interlocked.Increment(ref interestsRemovedSinceStartup);
        break;
    }
  }

  private static Dictionary<string, string> ToDictionary(StrongId id)
  {
    var dictionary = new Dictionary<string, string> { { "StrongIdTypeName", id.GetType().Name } };
    if (id.GetType().Namespace is { } ns)
    {
      dictionary.Add("StrongIdTypeNamespace", ns);
    }

    dictionary.Add("SerializedId", Serialization.Serialize(id));

    return dictionary;
  }
}
