# The hive release
Named after the fact that it uses a swarm of workers to handle read model hydration and todo task execution.

## Hydration workers
### Before
Before the hive release, the hydration operated in a purely reactive per-event hydration, with a queue manager, previously named "state machine"
```mermaid
sequenceDiagram
  participant Daemon
  participant Queue Manager
  participant Queue
  Daemon->>Queue Manager:Event, queue hydration func a
  Queue Manager->>+Queue:Parallelize
  Daemon->>Queue Manager:Event, queue hydration func b
  Queue Manager->>+Queue:Parallelize
  Daemon->>Queue Manager:Checkpoint received
  Queue Manager->>+Queue:Solve
  Queue->>Queue:Wait all queued solvers
  Queue->>-Queue Manager:Solved
  Queue->>-Queue Manager:Completed hydration func a
  Queue->>-Queue Manager:Completed hydration func b
  Queue Manager->>Daemon:Checkpoint completed
```
### After
## Todo task workers
### Before
### After
## Changes in consistency endpoints
### Before
### After
## Integration tests reliability and speed
### Before
### After