# Task
A job, integration or any other kind of action that reaches out to a remote service that is not already part of the framework.

> While every action that is not performed inside the memory of a program is technically an integration, the framework takes the file storage, the SQL server providing storage for the read models, and the Event Store DB integrations out of the equation when it comes to writing business logic.<br/>
All logic for command decisions, projected events, file storage, and read model projection is written with deterministic code, and the integrations are handled transparently.

The task is always performed as a reaction to an event, and the framework provides mechanisms to prevent the same event task from being executed multiple times.

## Shape
The data used when processing the task, only needs to implement an interface that acts as a formality, it does not force the class to implement any specific member:

```cs
public record WarnLowStockEmail(
  string ProductName,
  string DestinationAddress,
  string Content
) : TodoData;
```

## Definition
```cs
new TodoTaskDefinition<WarnLowStockEmail, Stock, StockRetrieved>
{
  Type = "warn-low-stock-email",
  Action = async (data, entity, entityFetcher, databaseFactory) =>
  {
    await emailSender(data.DestinationAddress, "Low stock alert", data.Content);
    return TodoOutcome.Done;
  },
  Originator = (evt, entity) =>
    new WarnLowStockEmail(
      entity.ProductName,
      settings.StockAlertRecipient,
      $"The product {entity.ProductName} is running low! Currently we have ${entity.Amount} left."
    ),
  SourcePrefix = Stock.StreamPrefix,
  Delay = TimeSpan.Zero,
  Expiration = TimeSpan.FromDays(7),
  LockLenght = TimeSpan.FromSeconds(90)
}
```

### Type
A string that uniquely identifies the task, must never change.

### Action
The action to perform when the task is executed, it receives the data, the entity, the fetcher, which allows you to fetch any other entity from the event store, and the database factory, which allows you to query read models.

It must return a TodoOutcome:
```cs
public enum TodoOutcome
{
  Retry, // Signals that the task should be retried.
  Done, // Signals that the task is done and no event is to be emitted.
  Locked // Not meant to be returned directly.
}
```
Or an `EventInsertion`, which works exactly like those of a [command](./command.md) and **must be emitted to the originating entity**. It is recommended to exclusively use the `AnyState` insertion unless you know very well what you are doing, as integrations are, by definition, unreliable, and the transactional guarantees might end up preventing the resulting event from being inserted.

### Originator
It generates the shape from the event and the entity, to be consumed at execution, must be deterministic.

### SourcePrefix
The stream prefix of the originating entity.

### Delay
The time to wait before the task is executed. Defaults to an immediate start.

### Expiration
The time after which the task is no longer valid, it counts from the time of creation plus the delay. Defaults to 7 days.

### LockLenght
The Task provides a pessimistic lock to guarantee that it is never executed more than once, that means that if an execution attempt starts, the task will not be executed again until the lock expires. Defaults to 90 seconds.

> Setting the LockLenght to a value too short might result in the task being executed multiple times, setting it too long might make retries challenging. It is recommended to convert tasks that require several integrations, hence take a long time to execute into a series of tasks that can be chained by the resulting events, this way the framework can focus on one step at a time and there is no need to handle previous failed attempt in the definition of the task.

### Dependencies
This is better explained in the [event model](./event-model.md) section, but the idea is that, since every task is meant to do a single integration against a single external party, the dependencies should be minimal, a configuration object and one or two functions should satisfy everything needed for the task.
