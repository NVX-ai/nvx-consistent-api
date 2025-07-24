# Entity
An entity represents the current state of the system when it comes to the concept being dealt with, for example: `Stock` represents the availability of a product.

The entity is built from `folding` all the [events](./event.md) of the [stream](../event-sourcing/stream.md) to end with a representation that usually needs to carry enough information to:

- Allow the [commands](./command.md) to make decisions.
- Project a [read model](./read-model.md).

An entity is built by defining its `shape`, which implements `EventModelEntity<Shape>` and `Fold<EventShape, EntityShape>` for every event in its stream:
```cs
// The entity itself:
public partial record Stock(Guid ProductId, int Amount, string ProductName, Guid? PictureId)
  : EventModelEntity<Stock>,
    Folds<StockAdded, Stock>,
    Folds<StockRetrieved, Stock>,
    Folds<StockNamed, Stock>,
    Folds<StockPictureAdded, Stock>
{
  public const string StreamPrefix = "entity-stock-";
  public string GetStreamName() => GetStreamName(ProductId);

  public ValueTask<Stock> Fold(StockAdded sa, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Amount = Amount + sa.Amount });

  public ValueTask<Stock> Fold(StockNamed sn, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { ProductName = sn.Name });

  public ValueTask<Stock> Fold(StockPictureAdded evt, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { PictureId = evt.PictureId });

  public ValueTask<Stock> Fold(StockRetrieved rs, EventMetadata metadata, RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Amount = Amount - rs.Amount });

  public static string GetStreamName(Guid productId) => $"{StreamPrefix}{productId}";
  public static Stock Defaulted(StrongGuid id) => new(id.Value, 0, "Unknown product", null);
}

// The definition, to "register" the entity in the framework:
new EntityDefinition<Stock, StrongGuid> { Defaulter = Stock.Defaulted, StreamPrefix = Stock.StreamPrefix },

```

Again, this is a simplified version, but as you can see, every event, through the implementation of the `Folds` interface, gets an implementation that will return a copy of the entity with a change.

Adding stock adds stock, and retrieving it reduces it, both of those events are the consequence of a [command](./command.md), while the picture added and the name change are the consequence of a [projection](./projection.md).

The definition is the artifact used by the framework to register the entity, it carries a function that allows to create the initial state of the entity, to then fold all the events, resulting in the current state of the entity.

The only challenging part of the entity shape is that one has to make sure that, after applying a single event, or a series of events that are always emitted on stream creation, the entity end in a valid state.
