# Nvx.ConsistentAPI

Event Modelling Framework, built and maintained by NVX.ai.

# Early Roadmap
1. XML docs for public interface.
1. Abstract the event store.
    1. Make the Event Store DB generic.
    1. Move the EventModelEvent interface back to the core project, out of the store.
    1. Implement a MS-SQL event store — there's an implementation for this already, if it can be integrated with minimal/no changes, if not, delay.
1. Deprecate the aggregating read models.
1. Gossip table for hydrations.
1. Attach an event model hash to the global checkpoint, so hydration is not adversarial.
1. Endpoint to get unused read model tables.
1. Endpoint to purge unused read model tables.
1. Abstract the read models.
    1. Make it Entity Framework, so we can switch to in-memory or postgres.
1. API generator function should return programmability utilities.
    1. Database handler.

# Documentation
[Head here to access the documentation](./docs/README.md).