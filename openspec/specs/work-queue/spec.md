# Work Queue Specification

## Purpose

Defines how workers claim work items from PostgreSQL and how ownership of a claimed item is maintained: the SKIP LOCKED claim with exact label matching, lease columns, the owner-guarded heartbeat, complete/fail, and the queued-depth count used by the scale supervisor (see compute).

## Requirements

### Requirement: Claim with SKIP LOCKED and label match

A claim SHALL atomically move the oldest `Queued` work item matching ALL of the worker's labels — `profile_key`, `image`, `profiles_ref` — to `Running`, stamping `leased_by`, `lease_until`, `heartbeat_at`, incrementing `attempt`, and returning the item's `id`, `run_id`, `profile_key`, `brief`, `lease_until` and `attempt`. The claim subselect SHALL use `FOR UPDATE SKIP LOCKED` ordered by `created_at` (FIFO within a profile) so concurrent claimers never block or double-claim.

#### Scenario: Two workers claim concurrently
- **WHEN** two workers with identical labels claim at the same moment
- **THEN** each receives a different item and neither waits on the other's locked row

#### Scenario: Label mismatch is a miss
- **WHEN** the only queued item has a different image digest or profiles ref
- **THEN** the claim returns no item (a value, not an error)

### Requirement: Claim miss is a value

An empty claim SHALL return a documented empty result (null / HTTP 204 No Content on the REST surface), never an exception. An empty queue is the normal state for a worker.

#### Scenario: Idle worker polls
- **WHEN** a worker claims and nothing matches its labels
- **THEN** the REST endpoint answers 204 with no body

### Requirement: Lease duration from options

The claim SHALL grant a lease of `LeaseTtl` from claim time (default 2 minutes, range 15 seconds–24 hours). Heartbeats extend by the same TTL.

#### Scenario: Lease stamped at claim
- **WHEN** an item is claimed at time T with the default TTL
- **THEN** `lease_until` is T + 2 minutes

### Requirement: Heartbeat owner guard

A heartbeat SHALL extend the lease (`lease_until`, `heartbeat_at`, `updated_at`) only when the row is the calling worker's (`leased_by` match), status is `Running`, AND the current lease has not expired. Any other case SHALL affect zero rows and report failure — the caller surfaces it as 409 Conflict (see worker-runtime).

#### Scenario: Wrong owner rejected
- **WHEN** worker B heartbeats an item leased to worker A
- **THEN** no row is updated and the REST call answers 409 `work-item.not-owner`

#### Scenario: Expired lease rejected
- **WHEN** a worker heartbeats after its lease expired (and the reaper or another claimer moved on)
- **THEN** the guard matches no row and the heartbeat fails

### Requirement: Complete

A complete SHALL move a `Running` item owned by the calling worker to `Succeeded`, clearing all lease columns, and append the `work_item.status_changed` journal entry with the worker's result JSON embedded as a structured value in the same transaction. The result MUST be non-empty valid JSON. Zero rows affected (unknown id, wrong owner, not running) SHALL report failure (409 on REST), not throw.

#### Scenario: Happy complete
- **WHEN** the owner posts a result JSON for its running item
- **THEN** the item is `Succeeded`, lease columns are null, and the journal carries the result

#### Scenario: Complete after requeue
- **WHEN** the item was reaped and requeued while a slow worker still holds it
- **THEN** the guarded update matches no row and complete fails with 409

### Requirement: Fail

A fail SHALL move a `Running` item owned by the calling worker to `Failed`, clearing lease columns, and append the journal entry with the human-readable reason embedded as a JSON string. The reason MUST be non-empty. Zero rows affected reports failure (409).

#### Scenario: Fail with reason
- **WHEN** the owner posts a failure reason
- **THEN** the item is `Failed` and the journal detail carries the reason text

### Requirement: Queued depth count

The queue SHALL expose a count of `Queued` items, optionally filtered to one profile key, for scale decisions and dashboards.

#### Scenario: Backlog per profile
- **WHEN** the scale supervisor asks for the queued count of profile `implement`
- **THEN** only `Queued` items with that profile key are counted

## ADAPTER Notes

The queue contract lives in `Comuki.Shared.Contracts.Queue` (`IWorkItemQueue`, `WorkItemLabels`, `ClaimedWorkItem`); the implementation is Postgres-only guarded raw SQL over the `work_items` table (see runs for the schema and the reaper).
