## Purpose

Defines durable user-facing Tasks between admission and execution: the
`WorkTask` aggregate, its lifecycle and resolution outcomes, sequential Run
attempts, source links, dependency graph, completion policies, and the Work
execution process that drives Orchestration through idempotent outbox/inbox
commands while serving standalone Task API and dashboard surfaces. A Task is
the durable identity; a Run is one execution attempt of that Task.

## ADDED Requirements

### Requirement: Standalone Task aggregate

The platform SHALL represent admitted work as a `WorkTask` carrying a project, title, versioned brief, one or more source references, lifecycle status, responsible actors, optional Mission association, and a CompletionPolicy. A `WorkTask` SHALL exist without a Mission and SHALL belong to at most one Mission. Standalone visibility SHALL follow project object policy; responsible assignment SHALL affect coordination and attention, never authorization.

#### Scenario: Tracker ticket becomes a Task
- **WHEN** an admitted tracker ticket is claimed
- **THEN** the platform creates exactly one `WorkTask` retaining the tracker source reference before dispatching any execution attempt, and a replay of the same claim returns the same `WorkTaskId`

### Requirement: Primary and related sources

A `WorkTask` SHALL aggregate one or more source references and SHALL designate at most one primary source for full lifecycle sync. Changing the primary source SHALL require an authorized Decision; the previous primary SHALL remain a related source with a link note. Related sources SHALL receive accepted key Decisions and the terminal summary, and SHALL NOT receive intermediate status events.

#### Scenario: Change primary tracker
- **WHEN** an authorized Decision changes the primary source
- **THEN** lifecycle sync targets the new primary, the previous primary is preserved as a related source with a link note, and intermediate status events do not fan out to the related sources

### Requirement: Task lifecycle and resolution

A `WorkTask` SHALL follow the deterministic state machine `Draft → Ready → Active ↔ Blocked → Resolved` with a side exit `Cancelled` available from any non-terminal status. A `Resolved` `WorkTask` SHALL carry exactly one outcome: `Succeeded`, `Waived`, `Replaced`, or `Failed`. Exhausting Run attempts SHALL move an Active Task to `Blocked` for an explicit retry, replacement, waiver, failed-resolution, or cancellation Decision; the Task SHALL NOT resolve automatically.

#### Scenario: Attempts exhausted
- **WHEN** the active Run terminates unsuccessfully and the retry policy has no remaining attempt
- **THEN** the `WorkTask` transitions to `Blocked` and awaits an explicit Decision; no `Resolved` transition occurs

#### Scenario: Cancellation is a side exit
- **WHEN** a `WorkTask` in `Draft`, `Ready`, `Active`, or `Blocked` receives a cancellation Decision
- **THEN** the Task transitions to `Cancelled` and no further transitions are accepted

### Requirement: Sequential Run attempts

Each `WorkTask` SHALL own an ordered sequence of Run attempts and SHALL have at most one active attempt. A user-visible retry or replacement SHALL create a new Run attempt with the next attempt ordinal; a terminal attempt SHALL NOT be resurrected. Infrastructure claim retries within one attempt SHALL remain separate from Task attempt numbering.

#### Scenario: Replacement creates a new attempt
- **WHEN** an authorized replacement Decision fences the active attempt
- **THEN** a new Run attempt with the next attempt ordinal becomes active, the previous attempt is durably fenced, and the `WorkTask` retains both attempt references in its attempt ledger

#### Scenario: One active attempt invariant
- **WHEN** two concurrent `DispatchRun` commands target the same `WorkTask`
- **THEN** exactly one command is accepted and emits an `Orchestration.StartRun`; the other is rejected without creating a second active attempt

### Requirement: Task relations

`WorkTask`s SHALL support acyclic directed `blocks` relations and symmetric informational `relates-to` relations. A blocked prerequisite SHALL be satisfied by `Succeeded`. `Waived` and `Replaced` SHALL satisfy or transfer each affected edge only as stated by their governing Decision. `Failed` SHALL NOT satisfy an edge automatically. An optional `WorkTask` SHALL block a required Mission `WorkTask` only when the edge is explicit.

#### Scenario: Failed prerequisite remains blocking
- **WHEN** a prerequisite `WorkTask` A resolves `Failed` and blocks `WorkTask` B
- **THEN** `WorkTask` B remains `Blocked` until a later Decision changes the relationship or resolution of A

### Requirement: Cross-Mission dependencies

An explicit `WorkTask` dependency MAY cross Missions in the same Project only when its creator has access to both Missions. A participant lacking access to the other Mission SHALL see a redacted external-dependency stub carrying the status class and a request-access or contact action, and SHALL NOT see the upstream Task's title, content, or artifacts.

#### Scenario: Private upstream dependency
- **WHEN** `WorkTask` B depends on `WorkTask` A in a Mission the viewer cannot access
- **THEN** B's dependency projection shows a redacted blocking-dependency stub with status class and a `request-access` action, and does not reveal A's title, content, or artifacts

### Requirement: Task dispatch policy

A `Ready` `WorkTask` SHALL be dispatched according to the effective project and Mission policy. Auto-dispatch SHALL remain bounded by authorization, autonomy, risk, budget, dependency satisfaction, and compute limits; outside those bounds the `WorkTask` SHALL wait for an explicit dispatch Decision.

#### Scenario: Manual policy holds ready work
- **WHEN** a `WorkTask` becomes `Ready` under a manual dispatch policy
- **THEN** no Run attempt is created until an authorized dispatch Decision is approved

### Requirement: Task visibility follows Mission attachment

A standalone `WorkTask` SHALL use project object policy. On first attachment to a private Mission, the `WorkTask` and all prior and future Runs and artifacts SHALL immediately require Mission access. Initial standalone-to-Mission attachment SHALL be allowed after prior Runs; transfer from one Mission to another SHALL be forbidden after the first Run.

#### Scenario: Prior artifact becomes private
- **WHEN** a standalone `WorkTask` with an existing Run artifact is promoted into a Mission
- **THEN** a project member who is not a Mission participant can no longer read that artifact

### Requirement: Work-Orchestration outbox dispatch

Work SHALL dispatch Run attempts and cancel in-flight attempts exclusively through durable outbox commands consumed by Orchestration's inbox. The Work outbox row SHALL be committed in the same transaction as the `WorkTask` state change that authorized the dispatch or cancel; Orchestration SHALL consume the command idempotently and SHALL respond with terminal `orchestration.run.terminated.v1` or `orchestration.run.cancelled.v1` events that Work consumes to advance the Task.

#### Scenario: Dispatch is durable
- **WHEN** a `Ready` `WorkTask` is dispatched
- **THEN** the `Orchestration.StartRun` command and the `WorkTask` `→ Active` state change are committed atomically, and a process restart before the command is dispatched does not lose the dispatch

#### Scenario: Duplicate terminal event is a no-op
- **WHEN** the same `orchestration.run.terminated.v1` is delivered twice for the same attempt
- **THEN** the second delivery is no-op'd via Work's inbox dedupe and the `WorkTask` advances exactly once

### Requirement: Admission idempotency

Admitting the same inbound item (webhook replay or claim retry) SHALL return the same `WorkTaskId` and SHALL NOT create a second `WorkTask`. Admission idempotency SHALL be keyed by the inbound item's external identity and SHALL hold across process restarts.

#### Scenario: Webhook replay returns the same Task
- **WHEN** an `integration.inbound.admitted.v1` event is delivered twice for the same inbound item id
- **THEN** the Work inbox returns the existing `WorkTaskId` and does not create a second Task

#### Scenario: Claim retry returns the same Task
- **WHEN** a claim command is retried for an inbound item that already has an admitted Task
- **THEN** the claim returns the existing `WorkTaskId` and the new claim attempt does not create a duplicate Task

### Requirement: Tracker sync on Task resolution with dedupe

Tracker synchronization SHALL move from Run terminal status to `WorkTask` resolution. One `WorkTask` SHALL produce exactly one final sync outcome regardless of how many attempts the Task ran through. The dedupe key SHALL be the `WorkTaskId` and the `WorkTask` version that produced the resolution.

#### Scenario: Failed-then-replaced Task produces one sync
- **WHEN** a `WorkTask`'s first attempt fails and a successful replacement attempt later resolves the Task `Succeeded`
- **THEN** the tracker receives one final sync outcome for the Task, and the intermediate failed attempt does not produce an intermediate tracker comment

#### Scenario: Replay of the same resolution is deduped
- **WHEN** the same `work.task.resolved.v1` event is delivered twice for the same Task and version
- **THEN** the second delivery is no-op'd via Work's inbox dedupe and the sync bridge emits at most one outbound sync job

### Requirement: Legacy Run backfill per cutover matrix

A one-shot backfill migration SHALL walk the existing `IncomingTicket`, `Run`, and `sync_job` rows and produce a deterministic outcome per row of the umbrella's existing-state cutover matrix. The migration SHALL be idempotent: every processed row SHALL carry a `task_backfill_marker` so a retried migration is a no-op on already-processed rows. The backfill SHALL refuse to enable the Task-based tracker sync bridge until the `sync_job` queue has been drained or every pending row carries a `drain_marker`.

#### Scenario: Pending ticket acquires a Task id
- **WHEN** the migration processes a Pending `IncomingTicket` row
- **THEN** the row acquires a non-null `WorkTaskId` and a `task_backfill_marker = 'pending'`

#### Scenario: Claimed with active Run becomes attempt 1
- **WHEN** the migration processes a Claimed `IncomingTicket` with an active Run
- **THEN** the WorkTask is created with `activeAttemptId` set to the legacy Run id and `attemptOrdinal = 1`, and a retried migration leaves the attempt ledger unchanged

#### Scenario: Terminal Run not yet synced is reconciled
- **WHEN** the migration processes a terminal Run with a still-pending `sync_job` row
- **THEN** one `WorkTask` outcome is created and one deduped sync job is emitted, and a retried migration produces zero new sync jobs

#### Scenario: Historical terminal Run is not promoted
- **WHEN** the migration processes a terminal Run with no pending sync job
- **THEN** the Run is recorded with `task_backfill_marker = 'historical-no-task'` and is surfaced by the compatibility projection as a legacy record, not promoted to a `WorkTask`

### Requirement: Run-consumer compatibility projection

Existing Run-shaped API, dashboard, and CLI consumers SHALL continue to function during the transition to `WorkTask`-authoritative state. The `WorkTaskRunView` read SHALL return a shape-compatible Run record for `GET /api/v1/work/tasks/{taskId}/run-view` and SHALL return 404 when the requested attempt has been superseded. The existing `GET /api/v1/runs/{runId}` list and detail responses SHALL be preserved at the URL level and SHALL include an authoritative `taskId` field on every row.

#### Scenario: Run-shaped read works during transition
- **WHEN** a client reads `GET /api/v1/runs/{runId}` for a Run whose `WorkTask` is authoritative
- **THEN** the response is shape-compatible with the pre-`WorkTask` shape and includes the `taskId` field naming the authoritative `WorkTask`

#### Scenario: Superseded attempt returns 404
- **WHEN** a client reads `GET /api/v1/work/tasks/{taskId}/run-view` for an attempt that has been fenced by a replacement
- **THEN** the response is 404 and the response body does not include the fenced attempt's terminal status

### Requirement: Responsible actors as attention metadata

A `WorkTask` SHALL carry one or more responsible human or service actors as attention metadata. Assignment SHALL NOT grant authorization and SHALL be revalidated against the actor's current grants and capacity before each effect. A human assignment SHALL require a distinct-human Decision proposal; a service-actor assignment MAY be auto-applied under the configured autonomy policy but SHALL honor the actor's grants and capacity.

#### Scenario: Assignment does not grant permissions
- **WHEN** a service actor is assigned to a `WorkTask`
- **THEN** the assignment appears as attention metadata on Task reads and the actor's permission catalog is unchanged

#### Scenario: Human self-assignment requires approval
- **WHEN** a human actor proposes to assign themselves to a `WorkTask`
- **THEN** the assignment is staged as a Decision proposal and becomes effective only after a distinct-human approval; a self-approval is rejected

#### Scenario: Over-capacity service assignment is rejected
- **WHEN** an auto-assignment would put a service actor above its declared `capacityHint`
- **THEN** the assignment is rejected with a `work.task.assignment-rejected.v1` event and the `WorkTask` remains unassigned for the next eligible actor

### Requirement: Task completion policy and reviewer separation

A `WorkTask` SHALL declare a `CompletionPolicy` (`Deterministic`, `BrainAssisted`, or `HumanReviewer`) and a versioned `EvidenceContract` enumerating the evidence kinds required for resolution. The `Work.Resolve` command SHALL require every `EvidenceContract` kind to be present and SHALL enforce reviewer separation: the actor who authored the latest replacement Decision SHALL NOT be the actor who approves the resulting Task resolution. A successful Run followed by a failed verification SHALL block the `WorkTask` without rewriting the successful Run's terminal status.

#### Scenario: Resolve without required evidence is rejected
- **WHEN** a `Resolve` command arrives whose `EvidenceContract` is incomplete
- **THEN** the command is rejected with `422 work.completion.evidence-incomplete` and the `WorkTask` remains in its current status

#### Scenario: Reviewer separation is enforced
- **WHEN** the actor authoring the latest replacement Decision attempts to approve the resulting Task resolution
- **THEN** the `Resolve` command is rejected with `409 work.completion.reviewer-separation` and the audit row records the rejection

#### Scenario: Successful Run plus failed verification blocks the Task
- **WHEN** a successful Run is followed by a verification attempt that fails the `EvidenceContract`
- **THEN** the `WorkTask` transitions to `Blocked` with a `WorkTaskRepairProposal` Decision opportunity, the successful Run's `run_events` journal stays immutable, and the Task does not resolve