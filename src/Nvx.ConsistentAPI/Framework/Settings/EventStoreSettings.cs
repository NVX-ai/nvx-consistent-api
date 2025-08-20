namespace Nvx.ConsistentAPI;

public interface EventStoreSettings
{
  public T Match<T>(
    Func<T> inMemory,
    Func<EventStoreDb, T> eventStoreDb,
    Func<MsSql, T> msSql) =>
    this switch
    {
      InMemory => inMemory(),
      EventStoreDb db => eventStoreDb(db),
      MsSql ms => msSql(ms),
      _ => throw new NotSupportedException("Unknown EventStoreSettings type")
    };

  public record InMemory : EventStoreSettings;

  public record EventStoreDb(string ConnectionString) : EventStoreSettings;

  public record MsSql(string ConnectionString) : EventStoreSettings;
}
