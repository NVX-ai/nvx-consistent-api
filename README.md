# Nvx.ConsistentAPI

Event Modelling Framework, built and maintained by NVX.ai.

# Early Roadmap
1. Document the framework. Currently, this framework is used internally, so documentation has been falling behind.
1. XML docs for public interface.
1. Abstract the event store.
    1. Implement an in memory event store for automated checks.
    1. Implement a MS-SQL event store — there's an implementation for this already, if it can be integrated with minimal/no changes, if not, delay.
1. Gossip table for hydrations, to be done while abstracting the event store, as the checkpoints need to change.
1. Attach an event model hash to the global checkpoint, so hydration is not adversarial, to be done while abstracting the event store, as the checkpoints need to change.
1. Deprecate the aggregating read models.
1. Endpoint to get unused read model tables.
1. Endpoint to purge unused read model tables.
1. Abstract the read models.
    1. Make it Entity Framework, so we can switch to in-memory or postgres.
1. API generator function should return programmability utilities.
    1. Fetcher.
    2. Database handler.
1. Expose those utilities via the test framework.

# Documentation
[Head here to access the documentation](./docs/README.md).