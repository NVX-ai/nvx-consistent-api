using Microsoft.Extensions.Logging;
using Nvx.ConsistentAPI.Framework;
using Nvx.ConsistentAPI.InternalTooling;
using Nvx.ConsistentAPI.Store.Store;

namespace Nvx.ConsistentAPI;

internal class DynamicConsistencyBoundaryDaemon(
  EventStore<EventModelEvent> store,
  InterestTrigger[] triggers,
  ILogger logger)
{
  private readonly InterestFetcher interestFetcher = new(store);
  private ulong? currentProcessedPosition;
  private ulong? currentSweepPosition;
  private int interestsRegisteredSinceStartup;
  private int interestsRemovedSinceStartup;

  private bool isSweepCompleted;

  public DynamicConsistencyBoundaryDaemonInsights Insights(ulong lastEventPositon) =>
    new(
      currentProcessedPosition ?? lastEventPositon,
      Math.Min(
        100,
        lastEventPositon == 0
          ? 100m
          : 100m * Convert.ToDecimal(currentProcessedPosition ?? 0) / Convert.ToDecimal(lastEventPositon)),
      currentSweepPosition,
      isSweepCompleted,
      interestsRegisteredSinceStartup,
      interestsRemovedSinceStartup,
      Math.Min(
        100,
        lastEventPositon == 0 || isSweepCompleted
          ? 100m
          : 100m * Convert.ToDecimal(currentSweepPosition ?? 0) / Convert.ToDecimal(lastEventPositon)));

  internal void Initialize()
  {
    _ = Task.Run(async () =>
    {
      var request = SubscribeAllRequest.FromNowOn();
      while (true)
      {
        try
        {
          await foreach (var solvedEvent in store.Subscribe(request).Events())
          {
            await TriggerInterests(solvedEvent.Event, solvedEvent.Metadata.EventId);
            request = SubscribeAllRequest.After(solvedEvent.Metadata.GlobalPosition);
            currentProcessedPosition = solvedEvent.Metadata.GlobalPosition;
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
      var request = ReadAllRequest.Start();
      while (shouldKeepRunning)
      {
        try
        {
          await foreach (var solvedEvent in store.Read(request).Events())
          {
            await TriggerInterests(solvedEvent.Event, solvedEvent.Metadata.EventId);
            request = ReadAllRequest.FromAndAfter(solvedEvent.Metadata.GlobalPosition);
            currentSweepPosition = solvedEvent.Metadata.GlobalPosition;
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

  private async Task TriggerInterests(EventModelEvent evt, Guid originatingEventId)
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
      .Select(s => (s,
        entities.FirstOrNone(ie => ie.InterestedEntityStreamName == s.InterestedEntityStreamName)))
      .Where(t => t.Item2.Match(
        e =>
          !e.OriginatingEventIds.Contains(originatingEventId)
          && e.ConcernedStreamNames.Contains(t.s.ConcernedEntityStreamName),
        () => false))
      .Select(t => t.s)
      .ToArray();

  private static EntityInterestManifest[] FilterOutIrrelevantStarts(
    EntityInterestManifest[] interests,
    InterestedEntityEntity[] entities,
    string originatingEventId) =>
    interests
      .Select(s => (s,
        entities.FirstOrNone(ie => ie.InterestedEntityStreamName == s.InterestedEntityStreamName)))
      .Where(t => t.Item2.Match(
        e =>
          !e.OriginatingEventIds.Contains(originatingEventId)
          && !e.ConcernedStreamNames.Contains(t.s.ConcernedEntityStreamName),
        () => true))
      .Select(t => t.s)
      .ToArray();

  private async Task RegisterNewInterests(EntityInterestManifest[] interests, Guid originatingEventId) =>
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

  private async Task RemoveInterests(EntityInterestManifest[] interests, Guid originatingEventId) =>
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
    await store.Insert(new InsertionPayload<EventModelEvent>(evt.GetSwimlane(), evt.GetEntityId(), [evt]));

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
