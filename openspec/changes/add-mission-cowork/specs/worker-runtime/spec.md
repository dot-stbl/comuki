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

### Requirement: Ephemeral worker tier
The WorkerRuntime SHALL admit a second tier of execution slots, `ephemeral`, alongside the existing slot tier. An ephemeral slot is identical in wire contract to a regular slot (claim, heartbeat, complete, fail on the same `IWorkerRuntime` surface; same event/command envelopes) but differs in lifecycle and storage:

- **Lifecycle** — an ephemeral slot is created per Mission (not per WorkItem) and is recycled across multiple WorkItems inside that Mission until the Mission terminates or the slot's container tears down. The slot's identity carries the parent `MissionId` in addition to the existing `ExecutionId` / fencing generation.
- **Scratch space** — each ephemeral slot owns a writable `/work/scratch/<executionId>/` directory inside its container, isolated from sibling slots on the same host. The scratch space persists across the slot's WorkItems until container teardown. A `Stop` on the slot destroys the scratch region.
- **ExecutionRequest origin** — the `ExecutionRequest.Origin` (per `add-durable-brain-operations`) MAY be `BrainResearch` for ephemeral slots; this is the only tier where the origin can be a Brain research operation that does not produce a user-visible WorkTask on its own.
- **Flush contract** — on successful WorkItem completion the slot SHALL flush designated `EphemeralNote` records into the platform Memory module under the parent Mission's scope (per the `memory` capability's "Ephemeral tier flush to platform memory" requirement). A flush failure SHALL NOT block terminal WorkItem publication but is reported via the WorkItem's `flushSummary`.

#### Scenario: Slot recycles within a Mission
- **WHEN** an ephemeral slot completes WorkItem `A` in Mission `M` and Brain issues another research request against `M`
- **THEN** the same ephemeral slot (same `WorkerHostId`, `SlotId`, parent `MissionId`) handles the next item without a fresh container; the scratch space from `A` is preserved between the two executions.

#### Scenario: Stop wipes scratch
- **WHEN** an ephemeral slot receives `Stop` mid-Mission
- **THEN** the slot's scratch directory is wiped; the WorkItem's terminal outcome is reported as `cancelled`; any unflushed `EphemeralNote` content is lost (a structured log entry names the lost fingerprints, mirroring `memory`'s "Lease lost before flush").

#### Scenario: Ephemeral slot cannot escape its Mission
- **WHEN** an ephemeral slot's slot worker is asked to handle a WorkItem whose Mission does not match the slot's parent `MissionId`
- **THEN** the claim is rejected with `409 work-item.mission-mismatch` (alongside the existing `409 work-item.not-owner`); the slot continues serving its own Mission.

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
