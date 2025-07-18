# Consistent API
A streamlined way to build Event Sourced back ends, with minimal setup and boilerplate.

## What do you need to get started
- An Event Store DB database, for the event log.
- A SQL Server database.
- An Azure Blob Storage account.
  - Or an Azurite emulator.

## What is Event Sourcing?
Event Sourcing is a software architectural pattern that captures the changes to an application's state as a sequence of immutable events.

These events don't just represent the transitions or transformations of the system's data over time, but actual, real world happenings.

Rather than storing the current state of the data, Event Sourcing stores a complete history of how what has happened, this approach allows you to reconstruct the current state at any point in time by replaying the events, even more, it allows you to reshape said state, as long as the events are meaningful for it.

## Why event sourcing?
Historically, organizations have always needed to maintain ledgers or logs to record transactions and any other kind of relevant happenstance. No shop that simply stores how much stock of any product they have can aspire to have any answer beyond: Do I have any?

Ledgers, initially implemented on paper (there is a seven thousands year ledger), and later in computer systems, have traditionally been vital for audit trails and compliance.

However, while the computation power of our systems increased, allowing us to build systems that kept track of more business issues, storage did not keep up the pace, meaning that storing everything that has happened in the system was not economically viable for most bussiness. Even with that, companies like banks still had to squeeze massive storage solutions in their budgets, because throwing away the history of the system was not an option.

For any other case: Enter Relational Database Management Systems (RDBMS) which allowed us to build storage efficient data models to represent the curren state of the system. The issue is that we, as humans, are still used to think in terms of what has happened.

As storage has become cheaper, there's really no excuse to keep building systems in a way that forces us to pretty much do forensics to understan what has happened, when we could have the history of our application be precisely that: A historical account of facts.

## Building blocks of event sourcing
### Event
An "Event" is the fundamental unit of information in the system. It represents an immutable record of something that has happened in the application. Events are typically expressed in a domain-specific language, in past tense and capture information relevant to the concept it represents, such as receiving an order, processing a payment, or registering that goods were stolen from the warehouse.

Once an event makes it into de ledger, it is considered "true", and any changes to be made should be done in the fashion of a correcting event. For example, if we were to declare that we received ten units of a product, to later find, when opening the box, that there were only six, we would register that there was a mistake made by our provider, and that we are missing four.

### Command
A "Command" is a request or intent from a user or external system to perform an action within the application. Unlike events, commands are not immutable; they are the a declaration of an intention, one to perform an action that will alter the system. When a command is executed, it can result in one or more events being generated as a consequence of the action.

### Read model
The "Read model" is a component of the Event Sourcing pattern that is responsible for serving query requests efficiently. It represents a denormalized, optimized view of the data derived from the events stored in the event log. Unlike the event store, which is optimized for writing events, the read model is designed for quick retrieval and display of data. It allows applications to efficiently retrieve and display information to end-users, often using various querying and indexing techniques.

A read model can be anything that has accesible information, records in a database exposed through an api, text files that are displayed by a monitor in the reception, even the color of a led light.

## Event Modeling
Event Modeling is a twist over Event Sourcing that aims to streamline most line of business applications, and it introduces three more concepts on top of the already existing for event sourcing:

### Swim lanes
Events are grouped together when they interact with the same concept of our application (known as an "aggregate" in Domain Driven Design), all events relating to a product's stock, would go into the stock lane, while event relating to purchases, would go into the order lane.

### Audiences
A system is consumed by a user, which can be a person, but also an application calling our API, in any case, those interactions will be grouped by one or more audiences. Customer support will not interact with our web shop in the same way as the marketing team or the buyers.

### Todo Tasks
A concept to abstract integrations. Any time our system needs to reach to the outside, a task is defined for every step of the integration, this removes the need for complex concepts like sagas.

## The framework
With the concepts introduced, we can now see how the framework allows us to implement an event model in the backend:

- [Getting started](./framework/getting-started.md).
- [Functional programming concepts](./framework/functional-programming-concepts.md).
- [The event](./framework/event.md).
- [The strong id](./framework/strong-id.md).
- [The entity](./framework/entity.md), the framework's representation of the swim lane.
- [The command](./framework/command.md).
- [The read model](./framework/read-model.md).
- [The task](./framework/todo-task.md).
  - [The recurring task](./framework/recurring-todo-task.md).
- [Projections](./framework/projection.md).
- [The event model](./framework/event-model.md).
- [Security](./framework/security.md).
- [Tenancy](./framework/tenancy.md).
- [Validation rules](./framework/validation-rules.md).
- [Automated tests](./framework/testing.md).
