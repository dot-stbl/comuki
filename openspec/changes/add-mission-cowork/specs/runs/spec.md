## MODIFIED Requirements

### Requirement: Run status set
A run SHALL represent one execution attempt of a Task and SHALL have exactly seven statuses: `Queued`, `Waiting`, `Running`, `Succeeded`, `Failed`, `Cancelled`, `Escalated`. A new Run SHALL be created in `Queued` and carry its Task id, attempt ordinal, execution generation, triggering actor/credential, and optional predecessor Run. At most one Run per Task may be active.

#### Scenario: Second Task attempt
- **WHEN** a blocked Task is approved for another attempt
- **THEN** a new Queued Run with the next ordinal is created and the terminal predecessor remains unchanged

#### Scenario: Run creation
- **WHEN** a Task dispatch creates its first Run
- **THEN** the Run is Queued with attempt ordinal 1 and a client-side generated UUIDv7 id

### Requirement: Run transition table
Run status changes SHALL be validated against one deterministic transition map. `Succeeded`, `Failed`, and `Cancelled` SHALL be terminal; a failed user-visible retry SHALL create a new Run rather than transition the same Run back to `Queued`. Infrastructure retries before terminal resolution belong to WorkItem claim attempts and do not resurrect a terminal Run.

#### Scenario: Failed run cannot be requeued
- **WHEN** a Failed Run is requested to retry
- **THEN** the Run remains Failed and the Work context decides whether to create another Task attempt

#### Scenario: Retry a failed run
- **WHEN** retry is approved for a Task whose latest Run is Failed
- **THEN** the failed Run stays terminal and a new Run attempt is created

#### Scenario: Terminal run is frozen
- **WHEN** a Run in Succeeded, Failed, or Cancelled is asked to transition
- **THEN** the transition is rejected as illegal

### Requirement: Atomic journal appends with queue mutations
Every queue mutation SHALL atomically append its audit journal row. Reconciliation of WorkItem outcomes SHALL deterministically move the parent Run to its terminal status once and publish an idempotent durable terminal integration event through an outbox distinct from best-effort realtime broadcasting.

#### Scenario: Final work item completes
- **WHEN** the last required WorkItem succeeds
- **THEN** the Run becomes Succeeded once and one terminal integration event is eventually delivered to the Work context

#### Scenario: Claim journals the transition
- **WHEN** a worker claims an item
- **THEN** the Running transition and its `work_item.status_changed` audit entry commit atomically

### Requirement: Run cancellation endpoint
Cancelling or superseding a non-terminal Run SHALL durably invalidate all live WorkItem executions before a replacement Run can activate. Heartbeat, complete, and fail SHALL verify the current execution generation in addition to lease ownership. A late result MAY be retained as non-authoritative evidence but SHALL NOT change Task or Run outcome.

#### Scenario: Late worker after replacement
- **WHEN** a worker attempts completion with a predecessor generation after replacement activates
- **THEN** authoritative completion is rejected and the new Task attempt remains unaffected

#### Scenario: Cancel an in-flight run with a reason
- **WHEN** an authorized caller cancels a Running Run with a reason
- **THEN** the Run and live execution generation are cancelled and the audit entry carries the reason

#### Scenario: Cancel an in-flight run without a reason
- **WHEN** an authorized caller cancels a Waiting Run without a reason
- **THEN** the Run is Cancelled and the audit entry omits the reason

#### Scenario: Cancel a terminal run returns 409
- **WHEN** an authorized caller cancels a Succeeded, Failed, or Cancelled Run
- **THEN** the response is 409 and the Run is unchanged

#### Scenario: Cancel an unknown run returns 404
- **WHEN** an authorized caller cancels a Run that is unknown or outside object scope
- **THEN** the response is 404 without disclosing foreign state
