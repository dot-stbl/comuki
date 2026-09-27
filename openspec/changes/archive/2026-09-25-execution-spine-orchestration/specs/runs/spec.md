## ADDED Requirements

### Requirement: WorkItem dependency readiness
A WorkItem created with unsatisfied dependencies SHALL enter `Blocked`
and SHALL NOT be eligible for claim while any `work_item_dependencies`
prerequisite has not reached `Succeeded`. The claim query SHALL match
only `Queued` items; a `Blocked` item SHALL transition to `Queued` when
its last unresolved prerequisite reaches `Succeeded`, evaluated in the
same transaction that finalizes that prerequisite's terminal status. A
prerequisite reaching `Failed` or `Cancelled` SHALL NOT silently unblock
its dependents; the dependent remains `Blocked` until the plan's
failure policy resolves it.

#### Scenario: Dependent cannot claim before its prerequisite succeeds
- **WHEN** a `Blocked` WorkItem's prerequisite is still `Queued` or
  `Running`
- **THEN** the dependent is not returned by any claim, regardless of
  matching profile/image/profilesRef labels

#### Scenario: Prerequisite success unblocks its dependent
- **WHEN** a WorkItem's last unresolved prerequisite transitions to
  `Succeeded`
- **THEN** the dependent moves `Blocked` → `Queued` in the same
  transaction and becomes claimable

#### Scenario: Prerequisite failure does not auto-unblock
- **WHEN** a WorkItem's prerequisite transitions to `Failed` or
  `Cancelled`
- **THEN** the dependent stays `Blocked`; the Run's plan-level failure
  policy decides its fate, not the claim path

### Requirement: Exactly-once Run terminal reconciliation
When the last WorkItem in a Run's plan reaches a terminal status, the
Run SHALL transition to exactly one terminal status derived from the
plan outcome (`Succeeded` when no WorkItem failed, `Failed` otherwise)
exactly once, regardless of how many WorkItems finalize concurrently.
The reconciliation SHALL be a single guarded store-level update (`WHERE
status NOT IN` the terminal set); no WorkItem finalizing after the Run
is already terminal SHALL re-trigger a Run transition.

#### Scenario: Concurrent final work items resolve to one Run transition
- **WHEN** the last two WorkItems of a Run's plan reach a terminal
  status in the same instant
- **THEN** exactly one Run terminal transition commits and exactly one
  terminal integration event is eventually published

### Requirement: Execution generation fencing on cancel and supersede
Every Run SHALL carry an execution `Generation`, starting at 1.
Cancelling a non-terminal Run, or activating a replacement Run attempt
for the same plan lineage, SHALL atomically bump the superseded Run's
`Generation` and mark every currently `Running` WorkItem under it as
fenced, in the same transaction as the Run's status transition. A
fenced WorkItem's lease SHALL be left intact for the reaper to reclaim
on timeout — fencing invalidates authority, it does not forge a lease
release.

#### Scenario: Cancel bumps the generation before the endpoint returns
- **WHEN** an authorized caller cancels a `Running` Run
- **THEN** the Run's generation increments and every live WorkItem
  under it is fenced in the same transaction that commits the
  `Cancelled` status

#### Scenario: Fenced lease still expires normally
- **WHEN** a fenced WorkItem's worker never calls back
- **THEN** the lease reaper reclaims it on the existing TTL/grace
  schedule — fencing does not bypass lease expiry

### Requirement: Durable terminal outbox
A Run reaching a terminal status SHALL enqueue a durable outbox message
in the same transaction as the terminal status change, distinct from
the existing best-effort SignalR realtime broadcast. A `BackgroundService`
dispatcher SHALL deliver queued messages at least once, using `FOR
UPDATE SKIP LOCKED` per-row leasing, bounded retries, and visible
dead-letter state for messages that exhaust retries. Consumers SHALL
dedupe by message id through a local inbox so at-least-once delivery is
exactly-once-observable.

#### Scenario: Terminal event survives a process crash
- **WHEN** the process commits a Run's terminal transition and crashes
  before the realtime broadcast fires
- **THEN** the outbox message for that transition is still delivered
  once the dispatcher resumes

#### Scenario: Realtime broadcast failure does not lose the durable fact
- **WHEN** the SignalR broadcast for a terminal Run fails or no client
  is connected
- **THEN** the outbox message is unaffected and is still delivered to
  durable consumers

#### Scenario: Poison message becomes visible dead-letter, not a silent drop
- **WHEN** a queued outbox message exhausts its bounded retry budget
- **THEN** it is marked dead-letter and visible to operators rather
  than retried forever or discarded

### Requirement: Internal event-contract compatibility
Orchestration's integration event types SHALL be named
`orchestration.<aggregate>.<past-tense>.v<major>` (for example
`orchestration.run.terminated.v1`). Within one major version, evolution
SHALL be additive-only — new optional fields, no removed or retyped
fields. A breaking payload change SHALL publish a new major type name
rather than mutate the existing one in place.

#### Scenario: Additive field change stays on the same major type
- **WHEN** a new optional field is added to
  `orchestration.run.terminated.v1`'s payload
- **THEN** existing consumers ignoring unknown fields continue to read
  it without a type-name change

#### Scenario: Breaking change requires a new major type name
- **WHEN** a payload change removes or retypes an existing field
- **THEN** the event ships as `orchestration.run.terminated.v2`
  alongside `.v1` until `.v1` consumers and outbox backlog are drained

### Requirement: Idempotent Run creation on admission
Given an admission claim identified by a stable message id, at most one
Run SHALL be created for it, regardless of how many times the admission
call is retried or delivered. Duplicate delivery of the same message id
SHALL be recognized and discarded by inbox dedupe before a second Run
is created.

#### Scenario: Retried admission creates one Run
- **WHEN** the same admission message id is delivered twice (retry
  after a timeout with no visible response)
- **THEN** exactly one Run exists for that message id after both
  deliveries are processed

#### Scenario: Concurrent admission delivery produces no orphan
- **WHEN** two concurrent admission attempts for the same message id
  race
- **THEN** exactly one Run is created and the losing attempt observes
  the same Run id, not an error

## MODIFIED Requirements

### Requirement: Run transition table
Run status changes SHALL be validated against a single table-driven
transition map shared by the aggregate guard and the application status
machine:

- `Queued` → `Waiting`, `Running`, `Failed`, `Cancelled`, `Escalated`
- `Waiting` → `Running`, `Failed`, `Cancelled`, `Escalated`
- `Running` → `Succeeded`, `Failed`, `Cancelled`, `Escalated`
- `Escalated` → `Running`, `Failed`, `Cancelled`
- `Succeeded`, `Failed`, `Cancelled` → terminal, no outgoing edges

An illegal transition SHALL throw; there SHALL be no other path to
mutate run status. `Failed` is terminal — a user-visible retry creates a
new Run attempt for the Task rather than transitioning the same Run
back to `Queued`; bounded WorkItem-level lease retries (the reaper's
`Running` → `Queued` requeue, see the work item transition table) are a
separate mechanism and are unaffected by this change.

#### Scenario: Retry a failed run
- **WHEN** retry is requested for a Task whose latest Run is Failed
- **THEN** the failed Run stays terminal and a new Run attempt is created

#### Scenario: Terminal run is frozen
- **WHEN** a run in `Succeeded`, `Failed`, or `Cancelled` is asked to
  transition
- **THEN** the transition is rejected as illegal

### Requirement: Run cancellation endpoint
`POST /api/v1/runs/{runId:guid}/cancel` (route constant
`ApiRoutes.RunCancel`) SHALL tear down a run that's still in flight.
The transition is legal from every non-terminal status (`Queued`,
`Waiting`, `Running`, `Escalated`) into `Cancelled`; terminal runs
(`Succeeded`, `Failed`, `Cancelled`) answer 409. Cancelling SHALL
atomically bump the Run's execution generation and fence every
currently `Running` WorkItem under it (see "Execution generation
fencing on cancel and supersede") in the same transaction as the status
change and journal append. When the request body carries an optional
`reason`, the journal entry's jsonb payload carries it as a `reason`
field. The same `run:read` permission gates the endpoint; an empty body
(`CancelRunRequest` with `reason: null`) is accepted.

#### Scenario: Cancel fences live executions before a replacement can activate
- **WHEN** an authorized caller cancels a `Running` Run that has live
  WorkItem executions
- **THEN** the Run's generation increments and every live WorkItem is
  fenced in the same transaction, before the response returns

#### Scenario: Late worker after replacement is rejected, not authoritative
- **WHEN** a worker completes a WorkItem using a generation from before
  the Run's cancellation or supersession
- **THEN** the completion is rejected (409) and the Task's replacement
  attempt is unaffected — the late result MAY be retained as
  non-authoritative evidence but SHALL NOT change Run or Task outcome

#### Scenario: Cancel an in-flight run with a reason
- **WHEN** an authorized caller cancels a Running Run with a reason
- **THEN** the Run and live execution generation are cancelled and the
  audit entry carries the reason

#### Scenario: Cancel an in-flight run without a reason
- **WHEN** an authorized caller cancels a Waiting Run without a reason
- **THEN** the Run is Cancelled and the audit entry omits the reason

#### Scenario: Cancel a terminal run returns 409
- **WHEN** an authorized caller cancels a Succeeded, Failed, or
  Cancelled Run
- **THEN** the response is 409 and the Run is unchanged

#### Scenario: Cancel an unknown run returns 404
- **WHEN** an authorized caller cancels a Run that is unknown or
  outside object scope
- **THEN** the response is 404 without disclosing foreign state

### Requirement: Persistence layout
Runs, work items, work-item dependencies (plan DAG edges), run events,
outbox messages, and inbox receipts SHALL be stored in snake_case
PostgreSQL tables `runs`, `work_items`, `work_item_dependencies`,
`run_events`, `outbox_messages`, `inbox_receipts` under the
orchestration context with its own migrations history. `runs` SHALL
carry a `generation` integer column (default 1); `work_items` SHALL
carry the `generation` it was claimed under. Claim-path indexes SHALL
be partial over live rows only: one over `(status, created_at)`
filtered to `Queued`/`Running`, one over `(profile_key, created_at)`
filtered to `Queued`. `outbox_messages` SHALL index
`(dispatched_at, created_at)` partial over undispatched rows for `FOR
UPDATE SKIP LOCKED` dispatch. Status values are stored as the PascalCase
enum names.

#### Scenario: Live-row index stays small
- **WHEN** the claim subselect scans the queue
- **THEN** it uses the partial `Queued`-only index rather than scanning
  terminal rows

#### Scenario: Outbox dispatch skips locked rows
- **WHEN** two dispatcher instances poll `outbox_messages` concurrently
- **THEN** each locks a disjoint set of undispatched rows via `FOR
  UPDATE SKIP LOCKED` and neither blocks the other
