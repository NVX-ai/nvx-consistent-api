using System.Runtime.CompilerServices;
using System.Text;
using Dapper;
using DeFuncto;
using DeFuncto.Extensions;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Nvx.ConsistentAPI.Store.Store;
using static DeFuncto.Prelude;

namespace Nvx.ConsistentAPI.Store.MsSql;

public class MsSqlEventStore<EventInterface>(
  string connectionString,
  Func<string, byte[], Option<(EventInterface evt, StrongId streamId)>> deserializer,
  Func<EventInterface, (string typeName, byte[] data)> serializer) : EventStore<EventInterface>
{
  private const string EventStoreTableCreationSql =
    """
    CREATE TABLE Events (
        EventId UNIQUEIDENTIFIER NOT NULL,
        GlobalPosition BIGINT IDENTITY(1,1) NOT NULL,
        StreamPosition BIGINT NOT NULL,
        InlinedStreamId NVARCHAR(255) NOT NULL,
        Swimlane NVARCHAR(255) NOT NULL,
        EventType NVARCHAR(255) NOT NULL,
        EventData VARBINARY(MAX) NOT NULL,
        Metadata VARBINARY(MAX) NULL,
        CONSTRAINT PK_Events PRIMARY KEY (GlobalPosition),
        CONSTRAINT UK_Events_EventId UNIQUE (EventId),
        CONSTRAINT UK_Events_Stream UNIQUE (Swimlane, InlinedStreamId, StreamPosition)
    );
    """;

  private const int BatchSize = 10;

  private readonly TimeSpan pollingDelay = TimeSpan.FromMilliseconds(500);

  public async Task Initialize(CancellationToken cancellationToken = default)
  {
    await using var connection = new SqlConnection(connectionString);
    try
    {
      await connection.ExecuteAsync(EventStoreTableCreationSql, cancellationToken);
    }
    catch (SqlException e) when (e.Number == 2714)
    {
      // Ignore if the table already exists.
    }
  }

  public async IAsyncEnumerable<ReadAllMessage<EventInterface>> Read(
    ReadAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await using var connection = new SqlConnection(connectionString);
    var hasNotifiedReadingStarted = false;

    var direction =
      request.Relative switch
      {
        RelativePosition.Start => "ASC",
        RelativePosition.End => "DESC",
        _ => request.Direction switch
        {
          ReadDirection.Forwards => "ASC",
          ReadDirection.Backwards => "DESC",
          _ => "ASC"
        }
      };

    var positionFilter =
      request.Direction switch
      {
        ReadDirection.Backwards => "GlobalPosition < @Position",
        _ => "GlobalPosition > @Position"
      };

    var position =
      request.Relative switch
      {
        RelativePosition.Start => 0L,
        RelativePosition.End => long.MaxValue,
        _ => request.Direction switch
        {
          ReadDirection.Forwards => (long)(request.Position.HasValue ? request.Position.Value - 1 : 0L),
          ReadDirection.Backwards => (long)(request.Position ?? long.MaxValue),
          _ => 0L
        }
      };


    var swimlaneFilters = request.Swimlanes is null || request.Swimlanes.Length == 0
      ? ""
      : $" AND Swimlane IN ({string.Join(", ", request.Swimlanes.Select(s => $"'{s}'"))})";

    while (!cancellationToken.IsCancellationRequested)
    {
      var query =
        $"""
           SELECT TOP {BatchSize} EventId, GlobalPosition, StreamPosition, InlinedStreamId, Swimlane, EventType, EventData, Metadata
           FROM Events
           WHERE {positionFilter}{swimlaneFilters}
           ORDER BY GlobalPosition {direction};
         """;
      var records = await connection
        .QueryAsync<FullEventRecord>(
          query,
          new
          {
            Position = (long?)position
          })
        .Map(r => r.ToArray());

      if (!hasNotifiedReadingStarted)
      {
        yield return new ReadAllMessage<EventInterface>.ReadingStarted();
        hasNotifiedReadingStarted = true;
      }

      if (records.Length == 0)
      {
        break;
      }

      foreach (var record in records)
      {
        var evt =
          from e in deserializer(record.EventType, record.EventData)
          from m in DeserializeMetadata(record.Metadata)
          select new ReadAllMessage<EventInterface>.AllEvent(
            record.Swimlane,
            e.streamId,
            e.evt,
            new StoredEventMetadata(
              record.EventId,
              m.EmittedBy,
              m.CorrelationId,
              m.CausationId,
              m.EmittedAt,
              (ulong)record.GlobalPosition,
              record.StreamPosition)) as ReadAllMessage<EventInterface>;
        yield return evt.DefaultValue(ReadAllMessage<EventInterface> () =>
          new ReadAllMessage<EventInterface>.ToxicAllEvent(
            record.Swimlane,
            record.InlinedStreamId,
            record.Metadata,
            (ulong)record.GlobalPosition,
            record.StreamPosition));
        position = record.GlobalPosition;
      }

      if (records.Length < BatchSize)
      {
        break;
      }
    }
  }

  public async IAsyncEnumerable<ReadAllMessage<EventInterface>> Subscribe(
    SubscribeAllRequest request = default,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var currentPosition = (long?)request.Position ?? await GetMaxGlobalPosition();
    currentPosition++;
    var hasNotifiedReadingStarted = false;

    var messageBatch = new List<ReadAllMessage<EventInterface>>(BatchSize);
    while (!cancellationToken.IsCancellationRequested)
    {
      var readRequest = new ReadAllRequest(
        (ulong?)currentPosition,
        request.Position == 0 && !hasNotifiedReadingStarted ? RelativePosition.Start : null,
        ReadDirection.Forwards,
        request.Swimlanes ?? []);
      messageBatch.Clear();

      try
      {
        await foreach (var message in Read(readRequest, cancellationToken))
        {
          messageBatch.Add(message);
        }
      }
      catch
      {
        // Ignore exceptions.
      }

      if (messageBatch.Count == 0)
      {
        await Task.Delay(pollingDelay, cancellationToken);
        continue;
      }

      foreach (var message in messageBatch)
      {
        if (!hasNotifiedReadingStarted)
        {
          yield return new ReadAllMessage<EventInterface>.ReadingStarted();
          hasNotifiedReadingStarted = true;
        }

        if (message is not ReadAllMessage<EventInterface>.ReadingStarted)
        {
          yield return message;
        }

        currentPosition = message switch
        {
          ReadAllMessage<EventInterface>.AllEvent e => (long)e.Metadata.GlobalPosition + 1,
          ReadAllMessage<EventInterface>.ToxicAllEvent te => (long)te.GlobalPosition + 1,
          _ => currentPosition
        };
      }
    }
  }

  public AsyncResult<InsertionSuccess, InsertionFailure> Insert(InsertionPayload<EventInterface> payload)
  {
    return Go();

    async Task<Result<InsertionSuccess, InsertionFailure>> Go()
    {
      try
      {
        await using var connection = new SqlConnection(connectionString);
        var payloadEventIds = payload.Insertions.Select(i => i.Metadata.EventId).ToArray();
        var insertionDefaultCorrelationId = Guid.NewGuid().ToString();

        while (true)
        {
          try
          {
            var alreadyExistingIds = await connection.QueryAsync<Guid>(
              """
              SELECT EventId
              FROM Events
              WHERE EventId IN @EventIds
              """,
              new { EventIds = payloadEventIds });

            var insertions = payload.Insertions.Where(i => !alreadyExistingIds.Contains(i.Metadata.EventId)).ToArray();

            if (insertions.Length > 100)
            {
              return new InsertionFailure.PayloadTooLarge();
            }

            var latestStreamEvent = await connection.QuerySingleOrDefaultAsync<EventExistenceRecord>(
              """
              SELECT TOP 1 GlobalPosition, StreamPosition
              FROM Events
              WHERE Swimlane = @Swimlane AND InlinedStreamId = @StreamId
              ORDER BY StreamPosition DESC
              """,
              new { payload.Swimlane, StreamId = payload.StreamId.StreamId() });

            if (insertions.Length == 0)
            {
              return new InsertionSuccess(
                (ulong?)latestStreamEvent?.GlobalPosition ?? 0UL,
                latestStreamEvent?.StreamPosition ?? 0);
            }

            var offset = payload.InsertionType switch
            {
              InsertAfter(var pos) => pos + 1,
              _ => latestStreamEvent?.StreamPosition switch
              {
                { } le => le + 1,
                null => 0
              }
            };

            var insertionRecords = insertions
              .Select((i, idx) => serializer(i.Event)
                .Apply(et => new EventInsertionRecord(
                  i.Metadata.EventId,
                  offset + idx,
                  payload.StreamId.StreamId(),
                  payload.Swimlane,
                  et.typeName,
                  et.data,
                  Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(
                      new StoredMetadata(
                        i.Metadata.RelatedUserSub,
                        i.Metadata.CorrelationId ?? insertionDefaultCorrelationId,
                        i.Metadata.CausationId,
                        i.Metadata.CreatedAt))))))
              .ToArray();

            var valueParams = insertionRecords
              .Select((_, i) =>
                $"(@EventId{i}, @StreamPosition{i}, @InlinedStreamId, @Swimlane, @EventType{i}, @EventData{i}, @Metadata{i})")
              .Apply(strings => string.Join(", ", strings));

            var values = insertionRecords
              .SelectMany<EventInsertionRecord, KeyValuePair<string, object>>((r, i) =>
              [
                new KeyValuePair<string, object>($"EventId{i}", r.EventId),
                new KeyValuePair<string, object>($"StreamPosition{i}", r.StreamPosition),
                new KeyValuePair<string, object>($"EventType{i}", r.EventType),
                new KeyValuePair<string, object>($"EventData{i}", r.EventData),
                new KeyValuePair<string, object>($"Metadata{i}", r.Metadata)
              ])
              .Append(new KeyValuePair<string, object>("InlinedStreamId", payload.StreamId.StreamId()))
              .Append(new KeyValuePair<string, object>("Swimlane", payload.Swimlane))
              .ToDictionary();

            var insertionSql =
              $"""
               INSERT INTO Events (EventId, StreamPosition, InlinedStreamId, Swimlane, EventType, EventData, Metadata)
               VALUES {valueParams};
               """;

            await connection.ExecuteAsync(insertionSql, values);

            var lastEvent = await connection.QuerySingleOrDefaultAsync<EventExistenceRecord>(
              """
              SELECT TOP 1 GlobalPosition, StreamPosition
              FROM Events
              WHERE EventId = @EventId
              """,
              new { insertionRecords.Last().EventId });

            return new InsertionSuccess((ulong)(lastEvent?.GlobalPosition ?? 0), lastEvent?.StreamPosition ?? 0);
          }
          catch (SqlException ex) when (
            ex.Number == 2627
            && ex.Message.Contains("UK_Events_Stream")
            && payload.InsertionType is not AnyStreamState)
          {
            return new InsertionFailure.ConsistencyCheckFailed();
          }
        }
      }
      catch
      {
        return new InsertionFailure.InsertionFailed();
      }
    }
  }

  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Read(
    ReadStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    await using var connection = new SqlConnection(connectionString);
    var direction = request.Direction == ReadDirection.Forwards ? "ASC" : "DESC";
    var positionFilter = request.Position switch
    {
      RelativePosition.Start => "1 = 1",
      RelativePosition.End => "1 = 1",
      _ => request.Direction switch
      {
        ReadDirection.Forwards => "StreamPosition > @StreamPosition",
        ReadDirection.Backwards => "StreamPosition < @StreamPosition",
        _ => "1 = 1"
      }
    };

    var streamPosition = request.Position switch
    {
      RelativePosition.Start => 0,
      RelativePosition.End => long.MaxValue,
      _ => request.Direction switch
      {
        ReadDirection.Forwards when request.StreamPosition.HasValue => request.StreamPosition - 1,
        ReadDirection.Backwards when request.StreamPosition.HasValue => request.StreamPosition + 1,
        _ => request.StreamPosition
      }
    };

    var offset = 0;
    var hasNotifiedReadingStarted = false;

    while (true)
    {
      var query =
        $"""
            SELECT EventId, GlobalPosition, StreamPosition, InlinedStreamId, Swimlane, EventType, EventData, Metadata
            FROM Events
            WHERE Swimlane = @Swimlane AND InlinedStreamId = @StreamId AND {positionFilter}
            ORDER BY GlobalPosition {direction}
            OFFSET @Offset ROWS
            FETCH NEXT @Count ROWS ONLY;
         """;

      var records = await connection
        .QueryAsync<FullEventRecord>(
          query,
          new
          {
            request.Swimlane,
            StreamId = request.Id.StreamId(),
            StreamPosition = streamPosition,
            Count = BatchSize,
            Offset = offset
          })
        .Map(r => r.ToArray());

      if (!hasNotifiedReadingStarted)
      {
        yield return new ReadStreamMessage<EventInterface>.ReadingStarted();
        hasNotifiedReadingStarted = true;
      }

      foreach (var record in records)
      {
        yield return (
            from e in deserializer(record.EventType, record.EventData)
            from m in DeserializeMetadata(record.Metadata)
            select new ReadStreamMessage<EventInterface>.SolvedEvent(
              record.Swimlane,
              e.streamId,
              e.evt,
              new StoredEventMetadata(
                record.EventId,
                m.EmittedBy,
                m.CorrelationId,
                m.CausationId,
                m.EmittedAt,
                (ulong)record.GlobalPosition,
                record.StreamPosition)) as ReadStreamMessage<EventInterface>)
          .DefaultValue(ReadStreamMessage<EventInterface> () => new ReadStreamMessage<EventInterface>.ToxicEvent(
            record.Swimlane,
            request.Id,
            record.EventData,
            record.Metadata,
            (ulong)record.GlobalPosition,
            record.StreamPosition));
      }

      if (records.Length < BatchSize)
      {
        break;
      }

      offset += BatchSize;
    }
  }


  public async IAsyncEnumerable<ReadStreamMessage<EventInterface>> Subscribe(
    SubscribeStreamRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var position = request.IsFromStart ? -1 : await GetMaxStreamPosition(request.Swimlane, request.Id.StreamId());

    var messageBatch = new List<ReadStreamMessage<EventInterface>>(BatchSize);

    var hasNotifiedReadingStarted = false;

    while (!cancellationToken.IsCancellationRequested)
    {
      var readRequest =
        new ReadStreamRequest(
          request.Swimlane,
          request.Id,
          request.IsFromStart && !hasNotifiedReadingStarted ? RelativePosition.Start : null,
          ReadDirection.Forwards,
          position);

      messageBatch.Clear();

      try
      {
        await foreach (var message in Read(readRequest, cancellationToken))
        {
          messageBatch.Add(message);
        }
      }
      catch
      {
        // Ignore exceptions.
      }

      if (!hasNotifiedReadingStarted)
      {
        yield return new ReadStreamMessage<EventInterface>.ReadingStarted();
        hasNotifiedReadingStarted = true;
      }

      if (messageBatch.Count == 0)
      {
        await Task.Delay(pollingDelay, cancellationToken);
        continue;
      }

      foreach (var message in messageBatch)
      {
        position = message switch
        {
          ReadStreamMessage<EventInterface>.SolvedEvent e => e.Metadata.StreamPosition,
          ReadStreamMessage<EventInterface>.ToxicEvent te => te.StreamPosition,
          _ => position
        };

        yield return message;
      }
    }
  }

  public async Task TruncateStream(string swimlane, StrongId id, long truncateBefore)
  {
    await using var connection = new SqlConnection(connectionString);
    await connection.ExecuteAsync(
      """
      DELETE FROM Events
      WHERE Swimlane = @Swimlane AND InlinedStreamId = @StreamId AND StreamPosition < @TruncateBefore
      """,
      new { Swimlane = swimlane, StreamId = id.StreamId(), TruncateBefore = truncateBefore });
  }

  private async Task<long> GetMaxGlobalPosition()
  {
    await using var connection = new SqlConnection(connectionString);
    return await connection.QueryFirstAsync<long>("SELECT COALESCE(MAX(GlobalPosition), -1) FROM Events");
  }

  private async Task<long> GetMaxStreamPosition(string swimlane, string inlinedStreamId)
  {
    await using var connection = new SqlConnection(connectionString);
    return await connection.QueryFirstAsync<long>(
      """
      SELECT COALESCE(MAX(StreamPosition), -1)
      FROM Events
      WHERE Swimlane = @Swimlane AND InlinedStreamId = @InlinedStreamId
      """,
      new { Swimlane = swimlane, InlinedStreamId = inlinedStreamId });
  }

  private static Option<StoredMetadata> DeserializeMetadata(byte[] metadata)
  {
    try
    {
      return Optional(JsonConvert.DeserializeObject<StoredMetadata>(Encoding.UTF8.GetString(metadata)));
    }
    catch
    {
      return None;
    }
  }

  private record FullEventRecord(
    Guid EventId,
    long GlobalPosition,
    long StreamPosition,
    string InlinedStreamId,
    string Swimlane,
    string EventType,
    byte[] EventData,
    byte[] Metadata);

  private record EventInsertionRecord(
    Guid EventId,
    long StreamPosition,
    string InlinedStreamId,
    string Swimlane,
    string EventType,
    byte[] EventData,
    byte[] Metadata);

  private record StoredMetadata(
    string? EmittedBy,
    string CorrelationId,
    string? CausationId,
    DateTime EmittedAt);

  private record EventExistenceRecord(long GlobalPosition, long StreamPosition);
}
