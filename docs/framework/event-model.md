# Event model
The puts all the artifacts together:
```cs
new EventModel
{
  Commands = Array.Empty<EventModelingCommandArtifact>(),
  ReadModels = Array.Empty<EventModelingReadModelArtifact>(),
  RecurringTasks = Array.Empty<RecurringTaskDefinition>(),
  Projections = Array.Empty<EventModelingProjectionArtifact>(),
  Tasks = Array.Empty<TodoTaskDefinition>(),
  Entities = Array.Empty<EntityDefinition>()
};
```
This is an empty event model, but illustrates all the elements that can be defined there.

It is recommended to create a sub model per Entity, and to use the `model.Merge(otherModel)` method to merge the two models into a single one, this way all the logic for a single entity can be defined in a single file, achieving high cohesion, while preventing coupling between entities by simply keeping them separate.
