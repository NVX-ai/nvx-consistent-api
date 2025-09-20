# The hive release
Named after the fact that it uses a swarm of workers to handle read model hydration and todo task execution.

## Hydration workers
### Before

```mermaid
sequenceDiagram
  participant Queue@{ "type" : "queue" }

  Daemon->>State Machine:Event, queue hydration func a
  State Machine->>+Queue:Parallelize
  Daemon->>State Machine:Event, queue hydration func b
  State Machine->>+Queue:Parallelize
  Daemon->>State Machine:Checkpoint received
  State Machine->>+Queue:Solve
  Queue->>Queue:Wait all queued solvers
  Queue->>-State Machine:Solved
  Queue->>-State Machine:Completed hydration func a
  Queue->>-State Machine:Completed hydration func b
  State Machine->>Daemon:Checkpoint completed
```
### After
```mermaid
sequenceDiagram

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