# The hive release
Named after the fact that it uses a swarm of workers to handle read model hydration and todo task execution.

## Hydration workers
### Before
Before the hive release, the hydration operated in a purely reactive per-event hydration, with a queue manager, previously named "state machine". When a new event arrived, the daemon would use the queue manager to start the hydration, releasing the daemon to receive another event to process.

The issue with this approach is, as you can see below, is that while the queue is blocked, be it because a checkpoint has been received, or because the queue is full, the next event that comes will wait, and since the daemon uses a grpc subscription, this can, and will, timeout.
```mermaid
sequenceDiagram
  participant Daemon
  participant Queue Manager
  participant Hydrations
  Daemon->>Queue Manager:Event 1, hydration func
  Queue Manager->>+Hydrations:Start hydration, parallelized
  Daemon->>Queue Manager:Event 2, hydration func
  Queue Manager->>+Hydrations:Start hydration, parallelized
  Daemon->>Queue Manager:Event 3, hydration func
  Queue Manager->>+Hydrations:Start hydration, parallelized
  Daemon->>Queue Manager:Checkpoint received
  Queue Manager->>+Hydrations:Solve
  Daemon->>+Daemon:Event 4, waiting
  Hydrations->>Hydrations:Wait all hydrations
  Hydrations->>-Queue Manager:Solved
  Hydrations->>-Queue Manager:Completed hydration event 1
  Hydrations->>-Queue Manager:Completed hydration event 2
  Hydrations->>-Queue Manager:Completed hydration event 3
  Queue Manager->>Daemon:Checkpoint completed
  Daemon->>-Queue Manager:Event 4, hydration func
  Queue Manager->>+Hydrations:Start hydration, parallelized
```
### After
With the introduction of the hydration queue and the workers, the daemon's only responsibility is to receive events and insert create a function that will insert a record in the hydration queue.
```mermaid
sequenceDiagram
  participant Daemon
  participant Queue Manager
  participant Queue Functions
  participant Hydration Queue
  participant Worker A
  participant Worker B
  Daemon->>Queue Manager:Event 1, queueing func
  Queue Manager->>+Queue Functions:Start, parallelized
  Daemon->>Queue Manager:Event 2, queueing func
  Queue Manager->>+Queue Functions:Start, parallelized
  Daemon->>Queue Manager:Event 3, queueing func
  Queue Manager->>+Queue Functions:Start, parallelized
  Daemon->>Queue Manager:Checkpoint received
  Queue Manager->>+Queue Functions:Solve
  Daemon->>+Daemon:Event 4, waiting
  Queue Functions->>Queue Functions:Wait all queue functions
  Queue Functions->>-Queue Manager:Solved
  Queue Functions->>-Queue Manager:Completed hydration event 1
  Queue Functions->>Hydration Queue:Queued
  Worker A->>Hydration Queue:Pick
  Worker A->>+Worker A:Hydrate
  Queue Functions->>-Queue Manager:Completed hydration event 2
  Queue Functions->>Hydration Queue:Queued
  Worker B->>Hydration Queue:Pick
  Worker B->>+Worker B:Hydrate
  Queue Functions->>-Queue Manager:Completed hydration event 3
  Queue Functions->>Hydration Queue:Queued
  Worker A->>Hydration Queue:Mark as hydrated
  deactivate Worker A
  Worker A->>Hydration Queue:Pick
  Worker A->>+Worker A:Hydrate
  Queue Manager->>Daemon:Checkpoint completed
  Daemon->>-Queue Manager:Event 4, queueing func
  Queue Manager->>+Queue Functions:Start, parallelized
  deactivate Worker A
  deactivate Worker B
```
## Todo task workers
### Before
### After
## Changes in consistency endpoints
### Before
### After
## Integration tests reliability and speed
### Before
### After