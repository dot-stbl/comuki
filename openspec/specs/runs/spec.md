# Runs Specification

## Purpose

Defines the run and work-item lifecycle: the status enums, the table-driven transition machines, the append-only run journal (`run_events`), and the lease-reaper policy that requeues or fails stalled work. A run is one goal from intake decomposed into a plan of work items; a work item is one profile launch inside that plan.

## Requirements

### Requirement: Run status set

A run SHALL have exactly seven statuses: `Queued`, `Waiting`, `Running`, `Succeeded`, `Failed`, `Cancelled`, `Escalated`. A new run SHALL be created in `Queued` — the only legal entry status.

#### Scenario: Run creation
- **WHEN** a run is created for a project
- **THEN** its status is `Queued` and it carries a client-side generated UUIDv7 id

### Requirement: Run transition table

Run status changes SHALL be validated against a single table-driven transition map shared by the aggregate guard and the application status machine:

- `Queued` → `Waiting`, `Running`, `Failed`, `Cancelled`, `Escalated`
- `Waiting` → `Running`, `Failed`, `Cancelled`, `Escalated`
- `Running` → `Succeeded`, `Failed`, `Cancelled`, `Escalated`
- `Escalated` → `Running`, `Failed`, `Cancelled`
- `Failed` → `Queued` (the retry edge)
- `Succeeded`, `Cancelled` → terminal, no outgoing edges

An illegal transition SHALL throw; there SHALL be no other path to mutate run status.

#### Scenario: Retry a failed run
- **WHEN** a run in `Failed` transitions
- **THEN** `Queued` is accepted and every other target is rejected

#### Scenario: Terminal run is frozen
- **WHEN** a run in `Succeeded` or `Cancelled` is asked to transition
- **THEN** the transition is rejected as illegal

### Requirement: Work item status set

A work item SHALL have exactly six statuses: `Blocked`, `Queued`, `Running`, `Succeeded`, `Failed`, `Cancelled`. The status set SHALL NOT contain a `Stalled` member — a stall is an event, and the item moves to `Failed` or back to `Queued` by reaper policy. A new work item SHALL start in `Queued` (no dependencies) or `Blocked` (unsatisfied dependencies); any other initial status SHALL be rejected at creation.

#### Scenario: Blocked entry
- **WHEN** a work item is created inside a plan with unsatisfied dependencies
- **THEN** its initial status is `Blocked` and the `Blocked` → `Queued` edge unblocks it later

### Requirement: Work item transition table

Work item status changes SHALL follow a single table-driven map:

- `Blocked` → `Queued`, `Failed`, `Cancelled`
- `Queued` → `Running`, `Failed`, `Cancelled`
- `Running` → `Succeeded`, `Failed`, `Cancelled`, `Queued` (the `Queued` edge is the lease-expiry requeue used by the reaper)
- `Failed` → `Queued` (the retry edge)
- `Succeeded`, `Cancelled` → terminal, no outgoing edges

#### Scenario: Requeue edge
- **WHEN** a running work item's lease expires and retries remain
- **THEN** the item moves `Running` → `Queued` through the requeue edge

### Requirement: Work item claim shape

A work item SHALL carry the claim labels `ProfileKey`, `Image`, `ProfilesRef` (worker matches on all three), a raw-JSON `Brief`, the lease columns `LeasedBy` / `LeaseUntil` / `HeartbeatAt`, and an `Attempt` counter that counts claims including requeue retries. Creation SHALL require non-empty profile key, image, profiles ref and brief. Assigning a lease SHALL be legal only from `Queued`, SHALL bump `Attempt` and SHALL move the item to `Running` (see work-queue for the SQL contract).

#### Scenario: Attempt counts claims
- **WHEN** an item is claimed, reaped, requeued and claimed again
- **THEN** its `Attempt` is 2

### Requirement: Append-only run journal

The platform SHALL keep an append-only `run_events` journal (one row per state change or worker report). Each entry SHALL carry a UUIDv7 id, the owning `RunId`, a stable dot.case `Type`, a raw-JSON (`jsonb`) payload, and `OccurredAt`. The journal SHALL never be updated or deleted.

Platform-owned event types SHALL be:

- `run.status_changed` — payload carries from/to and the actor
- `work_item.status_changed` — payload carries the item id, from/to, and (for claims) worker id and attempt, or (for terminal transitions) an embedded result/reason detail
- `worker.reported` — payload mirrors a translated pi stage event (see worker-runtime)
- `work_item.lease_expired` — payload carries the item id, from/to (`Queued` requeue or `Failed` after max attempts) and the attempt count

The type set SHALL be open — worker-reported events may carry their own dotted kinds.

#### Scenario: Timeline ordering
- **WHEN** the journal is read for a run
- **THEN** entries are ordered by `OccurredAt` then `Id` (the timeline index)

#### Scenario: Worker detail embedded
- **WHEN** a worker completes an item with a result JSON
- **THEN** the `work_item.status_changed` entry embeds the parsed result as a structured `detail` value in the same transaction as the status change

### Requirement: Atomic journal appends with queue mutations

Every work-queue mutation (claim, complete, fail) and every reaper sweep SHALL append its journal event in the same database transaction as the status change. A status change without its journal row SHALL not be observable.

#### Scenario: Claim journals the transition
- **WHEN** a worker claims an item
- **THEN** the `Running` transition and its `work_item.status_changed` event commit atomically

### Requirement: Lease reaper policy

A background reaper SHALL sweep expired work-item leases every `ReapInterval`. A lease is reaped when `lease_until` is at or before `now - ReapGrace` (the grace window absorbs clock skew and slow workers). The sweep SHALL decide per row:

- `attempt < MaxAttempts` → requeue: `Running` → `Queued`, lease columns cleared
- `attempt >= MaxAttempts` → fail: `Running` → `Failed`, lease columns cleared

Both decisions are set-based guarded SQL executed in one transaction; every reaped row SHALL be journalled as `work_item.lease_expired`. Defaults: lease TTL 2 minutes, reap interval 30 seconds, reap grace 30 seconds, max attempts 3; all bounded by validated ranges.

#### Scenario: Requeue with retries left
- **WHEN** a running item's lease expired past grace and attempt is below the budget
- **THEN** the item returns to `Queued` with lease columns cleared and a `work_item.lease_expired` entry naming `Queued`

#### Scenario: Fail after budget exhausted
- **WHEN** a running item's lease expired past grace and attempt reached max attempts
- **THEN** the item moves to `Failed` and the journal entry names `Failed`

#### Scenario: Heartbeat and reaper race
- **WHEN** a slow worker heartbeats while the reaper's sweep is in flight
- **THEN** the guarded updates resolve in the store — whichever guard matches first wins, and no double transition occurs

### Requirement: Persistence layout

Runs, work items, work-item dependencies (plan DAG edges) and run events SHALL be stored in snake_case PostgreSQL tables `runs`, `work_items`, `work_item_dependencies`, `run_events` under the orchestration context with its own migrations history. Claim-path indexes SHALL be partial over live rows only: one over `(status, created_at)` filtered to `Queued`/`Running`, one over `(profile_key, created_at)` filtered to `Queued`. Status values are stored as the PascalCase enum names.

#### Scenario: Live-row index stays small
- **WHEN** the claim subselect scans the queue
- **THEN** it uses the partial `Queued`-only index rather than scanning terminal rows

## ADAPTER Notes

Dependencies between work items (`work_item_dependencies`) exist as plan DAG edges with cascade delete from the parent run; no dependency-scheduling behavior is implemented yet — only the storage and the `Blocked` entry status.
