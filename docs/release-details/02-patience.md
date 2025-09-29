# Patience
Centered in the ergonomics of integration tests.
## Waiting for an event
`TestSetup` can now `WaitFor` an event/events to happen, be it via structural equality or by a predicate.
## Getting a read model without needing to wait for hydration
`ReadModel` and `ReadModels` have now an overload that fetches the entity and projects in memory.
This doesn't apply routing, filtering or security, it's meant to verify that the logic to project is solid.
It doesn't await for any kind of consistency, as is meant to be used after the emission of an event that will result in the desired new state.
## Waiting for a read model to satisfy a predicate
`ReadModelWhen` will use the new in memory overload to wait for a read model to satisfy a predicate.
## Fetching
`Fetch` allows for direct entity fetching in the integration tests.
## Wait for ToDo
`WaitForTodo` allows developers to wait for a todo task to execute successfully related to an entity.

This will allow for precision-waiting in integration tests that depend on tasks being executed and projections happening.