## ADDED Requirements

### Requirement: General execution request origin
Orchestration SHALL accept execution requests with one explicit origin: `WorkTask`, `BrainResearch`, `Verification`, `Discovery`, `ScheduledJob`, or `Maintenance`. Every Run and WorkItem SHALL preserve the origin reference and causation. `BrainResearch` links to a Brain operation/delegation and SHALL NOT create a user-visible or hidden WorkTask.

#### Scenario: Subbrain requests repository research
- **WHEN** a subbrain submits a bounded research execution request
- **THEN** workers execute it under ordinary leases and its result returns to the Brain operation trace without appearing in the Task board

## MODIFIED Requirements

### Requirement: Stream semantics — end on events completion
Each execution slot SHALL open an independently authenticated command/event stream bound to WorkerHostId, SlotId, ExecutionId, WorkItemId, and fencing generation. Multiple slot streams from one host MAY coexist; opening one SHALL NOT replace another. The call ends when that execution's event enumeration completes.

#### Scenario: Two slots connect
- **WHEN** two slots on one warm host open streams concurrently
- **THEN** both remain active and commands are routed only to the addressed execution

#### Scenario: Report then complete ends the call
- **WHEN** one slot finishes its event stream after sending the final StageReport
- **THEN** that slot's command loop ends and its stream closes without affecting sibling slots

### Requirement: Worker REST surface
The worker REST surface SHALL claim, heartbeat, complete, and fail on behalf of one authenticated execution slot. Claim results and subsequent mutations SHALL carry an execution id and fencing generation. Ownership is derived from the host/slot credential and server assignment; a slot cannot claim another slot's identity.

#### Scenario: Wrong slot rejected
- **WHEN** slot B attempts to complete a WorkItem leased to slot A
- **THEN** the mutation is rejected without changing the WorkItem

#### Scenario: Ownership miss is 409, not 500
- **WHEN** a slot completes an item the reaper already requeued or fenced
- **THEN** the endpoint answers 409 with a stable ownership code

### Requirement: Translator loop
A warm Translator host SHALL run a bounded number of independent slot loops. Each loop performs claim → isolated workspace preparation → agent process → stream/report → completion and repeats until stopped. Failure of one slot loop SHALL not stop healthy sibling slots; host-fatal failures terminate the host and leases recover each execution independently.

#### Scenario: One agent process fails
- **WHEN** an agent process exits non-zero in one of twenty slots
- **THEN** that WorkItem follows failure policy while the other nineteen slot loops continue

#### Scenario: Non-zero pi exit fails the item
- **WHEN** a spawned pi process exits non-zero
- **THEN** that execution reports failed with the exit code and safe stderr detail

#### Scenario: Lease lost mid-run
- **WHEN** a heartbeat is rejected while the slot agent still runs
- **THEN** that agent is cancelled and the slot does not complete or fail the item authoritatively

#### Scenario: Result text is authoritative
- **WHEN** an agent streams deltas and later emits authoritative final assistant text
- **THEN** the execution result uses the authoritative final wording

### Requirement: Orchestrator command handling in the worker
`Stop`, `InjectContext`, and `LeaseExpired` SHALL target one ExecutionId. Context injection is authoritative only when the selected agent runtime declares live-session injection support; otherwise the platform SHALL stage a new research/repair WorkItem rather than pretending a running no-session agent consumed the file.

#### Scenario: Runtime lacks live injection
- **WHEN** context is added for an agent launched without a resumable session
- **THEN** the system creates a follow-up execution path and does not report the original process as updated

#### Scenario: Soft stop
- **WHEN** the orchestrator sends Stop for one execution with a reason
- **THEN** only that execution's process tree is cancelled and its report is cancelled
