# Event
In the Consistent API framework, an event is represented as anything that has happened in the system, it always carries enough information to identify its [entity](./entity.md), and the type itself is informative.

As an example, these two events carry the same fields:
```cs
public record StockAdded(Guid ProductId, int Amount) : EventModelEvent
{
  public string GetStreamName() => Stock.GetStreamName(ProductId);
  public StrongId GetEntityId() => new StrongGuid(ProductId);
}

public record StockRetrieved(Guid ProductId, int Amount) : EventModelEvent
{
  public string GetStreamName() => Stock.GetStreamName(ProductId);
  public StrongId GetEntityId() => new StrongGuid(ProductId);
}
```

It will, though, be evident for the reader that one implies that stock was added, while the other that stock was retrieved (these events are an oversimplification of what happens in a real warehouse).

The interface `EventModelEvent` looks like this:
```cs
public interface EventModelEvent
{
  public string EventType => GetType().Apply(Naming.ToSpinalCase);
  string GetStreamName();
  public byte[] ToBytes() => EventSerialization.ToBytes(this);
  StrongId GetEntityId();
}
```

The [stream](../event-sourcing/stream.md) referenced there, is the ledger for the entity we are dealing with, in this case, it would be the product stock.
