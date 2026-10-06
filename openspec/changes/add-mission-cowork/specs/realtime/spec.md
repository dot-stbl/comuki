## ADDED Requirements

### Requirement: Join Mission stream
The realtime surface SHALL allow an authenticated client to join `mission:{missionId}` only after the common Mission access decision succeeds. Durable stream envelopes carry entry id, committed sequence, kind, schema version, references, occurred time, and a typed or safely ignorable payload. Unknown future kinds SHALL not disconnect older clients.

#### Scenario: Reconnect catches up
- **WHEN** a client misses entries while disconnected
- **THEN** it reads REST entries after its last committed sequence, deduplicates, and rejoins without gaps or duplicates

### Requirement: Multi-node presence and typing
Mission presence SHALL aggregate multiple connections per actor across Host replicas and expose online roster and expiring typing state. Presence and typing are ephemeral, do not consume durable sequence, and disappear after disconnect or TTL. Deployment configuration selects InMemory for declared single-node topology or Redis for multi-node topology; unsafe multi-node InMemory configuration fails startup/readiness. Redis outage degrades to durable REST stream and polling, marks presence/typing/cross-node push unavailable, and never serves knowingly partial roster as complete.

#### Scenario: One user has two clients
- **WHEN** the same participant connects from dashboard and CLI
- **THEN** the roster shows one actor with two connections and remains online until both disconnect

### Requirement: Mission attention delivery
Mentions, invitations, approval requests, blocking failures, and completion reviews SHALL create participant-scoped attention events. Ordinary messages and low-level worker progress SHALL not notify every participant by default. Per-participant last-read sequence and pending-action counts remain durable even though delivery is realtime.

#### Scenario: Mention while room closed
- **WHEN** a participant is mentioned while not viewing the Mission
- **THEN** the participant's attention inbox receives the event without disclosing it to non-participants

### Requirement: Realtime delivery contract
SignalR push SHALL be an at-most-once latency hint; durable REST cursor reads are authoritative. Mission stream contains milestones and summarized progress, not every worker chunk/tool activity. Clients use entry id and sequence for dedupe/catch-up and SHALL NOT treat push receipt as commit proof.

#### Scenario: Push lost
- **WHEN** a committed Mission entry is not delivered over SignalR
- **THEN** the client obtains it on the next cursor catch-up without data loss

### Requirement: Swarm-blackboard channel via IRealtimeBackplane
The realtime layer SHALL expose a per-Mission pub/sub channel for worker swarm findings on the existing `IRealtimeBackplane` (same deployment-configured topology: single-node InMemory or multi-node Redis; same unsafe-multi-node-InMemory startup failure; same Redis-outage degradation to durable-only polling — never knowingly partial). The channel key SHALL be `mission:swarm:<missionId>`; payloads SHALL carry `(fingerprint, workerKey, generation, op)` where `op ∈ { write, supersede, expire }`. The channel SHALL NOT consume Mission stream sequence (presence and swarm-blackboard channels both live outside the append log); it SHALL be available only after the common Mission access decision (`Multi-node presence and typing` requirement's access check applies unchanged to this channel).

#### Scenario: Subscribed worker receives swarm write
- **WHEN** worker `W` in Mission `M` subscribes to `mission:swarm:M` and worker `V` in the same Mission publishes a finding with fingerprint `F`
- **THEN** `W` receives exactly one `BlackboardWrite` event (`op=write, fingerprint=F, workerKey=V, generation=<gen>`) within the configured push latency hint.

#### Scenario: Cross-Mission isolation enforced at the realtime layer
- **WHEN** a worker in Mission `M1` attempts to subscribe to `mission:swarm:M2` without `M2` membership
- **THEN** the subscription is denied with the same `403 mission.access.denied` ProblemDetails the Mission stream returns — channels share the access decision, not the data plane.

#### Scenario: Redis outage does not corrupt the swarm channel
- **WHEN** the configured backplane is `Redis` and the connection to the broker fails
- **THEN** the swarm-blackboard channel degrades to durable-only polling (workers fall back to `IMemoryStore.Search` for `kind=BlackboardFinding` under the Mission scope) and a presence/swarm-unavailable marker is published; no `BlackboardWrite` event is silently dropped without a fallback path being advertised to subscribers.
