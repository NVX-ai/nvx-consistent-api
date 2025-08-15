namespace Nvx.ConsistentAPI;

public interface EventStoreSettings
{
  public T Match<T>(
    Func<T> inMemory,
    Func<EventStoreDb, T> eventStoreDb) =>
    this switch
    {
      InMemory => inMemory(),
      EventStoreDb db => eventStoreDb(db),
      _ => throw new NotSupportedException("Unknown EventStoreSettings type")
    };

  public record InMemory : EventStoreSettings;

  public record EventStoreDb(string ConnectionString) : EventStoreSettings;
}
