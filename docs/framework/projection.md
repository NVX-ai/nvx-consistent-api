# Projection
In Event Sourcing, anything that has a state derived from a stream, is a projection, meaning that the read models are also projections, but in the context of Consistent API, projections are events that are generated exclusively as a consequence of another event.

## Shape
The shape of a projection is just an Event, that has to be Folded by an entity. That event and entity will be referred to as `ProjectedEvent` and `ProjectedEntity`, bear in mind that an entity does not have to be 100% projected, and can fold events emitted from other projections, commands or tasks, but the Projected event should always be the result of a projection, this rule enforces the low level of coupling that charaterizes event sourced systems. This rule can be violated as long as the coupling is acknowledged and found worth it. The other two are the `SourceEvent` and `SourceEntity` respectively.

## Definition
```cs
private class ProductCreatedToStockNamed : Projector<ProductCreated, StockNamed, Product, Stock>
{
  public Option<StockNamed> Project(
    ProductCreated sourceEvent,
    Product sourceEntity,
    Option<Stock> destinationEntity,
    Uuid eventId,
    DateTime createdAt) =>
    new StockNamed(sourceEntity.Id, sourceEntity.Name);
}

new ProjectionDefinition<ProductCreated, StockNamed, Product, Stock>
{
  Name = "projection-product-created-to-stock-named",
  Projector = new ProductCreatedToStockNamed(),
  SourcePrefix = Product.StreamPrefix,
  GetProjectionIds = (sourceEvent, sourceEntity, eventId) => new []{ p.EntityId }
}
```
> This projection emits the name of a product to the stock entity, this way we can keep the stream that keeps track of the definition of a product short, and have a stock entity that keeps track of the stock, having a "noisier" history.

### Projector
A function that receives the source event, the source entity, and the destination entity, or `None`, if the projected entity does not exist yet, the source event ID, which helps to deterministically track the projection, should it be needed, the creation time of the source event, and returns the projected event or `None` if the business logic determines that the projection is not needed.

### Name
The name of the projection, must be unique and immutable, changing it will create a new projection.

### SourcePrefix
The stream prefix of the source entity, this is used to subscribe to the originating event.

### GetProjectionIds
A projection will often project a single event, but it is possible that it will generate more than one, so this function will generate one Id per projected event. An example could be a group message in a chat application, one might want to generate a notification event for every member of the group.
