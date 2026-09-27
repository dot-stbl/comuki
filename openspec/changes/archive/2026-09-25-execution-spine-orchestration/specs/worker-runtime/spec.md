## MODIFIED Requirements

### Requirement: Worker REST surface

The worker REST API SHALL expose, all authenticated by the worker token
in the `Authorization` header (401 `worker.unauthenticated` ProblemDetails
otherwise):

- `POST /workers/claim` — body: image, profilesRef, profileKey; 200
  with the claimed item (workItemId, runId, profileKey, brief,
  leaseUntil unix-ms, attempt, generation) or 204 when the queue has
  nothing; 400 on validation failure
- `POST /workers/{workItemId}/heartbeat` — extends the lease; body
  carries the claimed generation; 204 held, 409 `work-item.not-owner`
  when the item is unknown, not running, leased to another worker, or
  leased at a stale generation (the Run was cancelled or superseded
  since claim)
- `POST /workers/{workItemId}/complete` — body: non-empty result JSON
  plus the claimed generation; 204 / 409 ownership-or-generation
- `POST /workers/{workItemId}/fail` — body: non-empty reason plus the
  claimed generation; 204 / 409 ownership-or-generation

The worker id the queue sees is the one the token was issued for —
ownership is token-derived, never claimed. A generation mismatch
answers the same 409 `work-item.not-owner` code as an ownership miss —
from the worker's perspective, losing a stale generation and losing a
lease to another worker are the same "you no longer hold this" outcome;
this change does not introduce a second 409 code. This requirement
covers the single-worker `WorkerId`+lease ownership model that exists
today; a per-slot `WorkerHostId`/`SlotId`/`ExecutionId` ownership model
is out of scope here (see `add-worker-pools-and-isolation-classes`).

#### Scenario: Ownership miss is 409, not 500
- **WHEN** a worker completes an item the reaper already requeued
- **THEN** the answer is 409 with code `work-item.not-owner`

#### Scenario: Stale generation after cancellation is rejected
- **WHEN** a worker completes a WorkItem using the generation it
  claimed under, after the owning Run was cancelled or superseded
  (generation bumped)
- **THEN** the answer is 409 with code `work-item.not-owner` and the
  WorkItem/Run outcome is unaffected by the late completion

#### Scenario: Current generation still succeeds
- **WHEN** a worker completes a WorkItem using the Run's current
  generation
- **THEN** the completion is accepted exactly as before this change
