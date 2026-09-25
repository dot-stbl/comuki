## Context

This change implements Gate B from `add-mission-cowork/design.md`
("Before Task/Mission automation, land WorkItem dependency readiness,
Run reconciliation, cancellation fencing, terminal outbox, and intake
claim idempotency. Existing behavior remains externally compatible.")
and closes `add-mission-cowork/decomposition.md`'s row for issue #87,
epic task ids §2 (2.1–2.7).

Verified against `platform/src` on the current branch:

- `RunStatus` (`Comuki.Engine.Orchestration/Domain/RunStatus.cs`) already
  has the exact target seven-value set (`Queued`, `Waiting`, `Running`,
  `Succeeded`, `Failed`, `Cancelled`, `Escalated`) and `WorkItemStatus`
  already has the target six-value set (`Blocked`, `Queued`, `Running`,
  `Succeeded`, `Failed`, `Cancelled`). No status-set change is needed —
  only transition-table and enforcement changes.
- `WorkItemQueueSql.ClaimSql` (`Infrastructure/Queue/WorkItemQueueSql.cs`)
  matches only `status = 'Queued' AND profile_key/image/profiles_ref` —
  `work_item_dependencies` rows are stored (with cascade delete from the
  parent run, per the current `openspec/specs/runs/spec.md` "ADAPTER
  Notes" section, which explicitly says "no dependency-scheduling
  behavior is implemented yet") but never consulted at claim.
- Commit `a6e9df17` ("drive run status from work item transitions")
  already lands the first reconciliation slice: activation-on-first-claim
  and finalize-on-last-terminal-item, guarded `UPDATE`s in
  `WorkItemQueueSql.cs`/`WorkItemQueueEf.cs`. This change proves that
  slice exactly-once under concurrency; it does not re-implement it.
- `RunTransitions.table`
  (`Domain/Runs/RunTransitions.cs`) has
  `[RunStatus.Failed] = [RunStatus.Queued]` today, and the current
  `openspec/specs/runs/spec.md` documents that edge as the intended
  contract. This change removes it.
- `HostCancelRunAdapter`
  (`Comuki.Host/Runs/HostCancelRunAdapter.cs`) already transitions a
  non-terminal Run to `Cancelled` and journals a `run.status_changed`
  event in the same transaction — it does not touch live WorkItem leases
  at all today.
- No `Generation`/fencing concept exists anywhere in
  `Comuki.Engine.Orchestration`. The only repo-wide "fenc*" hits are
  `Comuki.Engine.Compute`'s Docker network egress fence
  (`DockerEgressFence`, `ComputeFenceException`) — an unrelated concept
  (network isolation, not execution-generation fencing).
  `Comuki.Host/Workers/Api/WorkerEndpoints.cs` and its request/response
  types (`ClaimedWorkItemResponse`, `CompleteWorkItemRequest`,
  `FailWorkItemRequest`) carry no generation field; ownership is purely
  `WorkerId` + `LeasedBy`/`LeaseUntil`/`Attempt`, matching the current
  `openspec/specs/worker-runtime/spec.md` "Worker REST surface"
  requirement.
- No `Comuki.Shared.Messaging`, `IOutbox`, `outbox_messages`, or
  `inbox_receipts` exist anywhere in `platform/src` — fully greenfield,
  despite `add-mission-cowork/architecture.md` decision #3 declaring
  every context needs local `outbox_messages`/`inbox_receipts`. The
  existing `RunEvent` append-only journal
  (`Domain/Journal/RunEvent.cs`) and `SignalRRunEventsBroadcaster`
  (`Comuki.Host/Realtime/Broadcasting/`) remain exactly what they are —
  audit timeline and best-effort realtime push — and are explicitly not
  repurposed as the outbox (architecture.md decision #3 rejects
  "treating `run_events` or SignalR interceptor as an outbox").
- `IntakeRunLauncher.LaunchAsync`
  (`Comuki.Host/Intake/IntakeRunLauncher.cs`) creates a `Run` and its
  first `WorkItem` in one direct, synchronous `SaveChangesAsync` call
  with no message-id dedupe — the orphan/double-launch race task 2.5
  targets.

## Goals / Non-Goals

**Goals:** dependency-gated claim; exactly-once Run terminal
reconciliation; immutable terminal Run status; execution generation
fencing on cancel/supersede covering heartbeat/complete/fail; a durable
terminal outbox distinct from realtime broadcast; an internal
event-contract compatibility gate; idempotent Run creation on admission;
one crown scenario proving all of the above together.

**Non-Goals:** `WorkerHostId`/`SlotId`/multi-slot warm hosts (#100); any
Task/Mission concept (#89, #93); a generic saga/workflow engine; changes
to the worker gRPC stream contract, Translator loop, or container image;
changes to Intake's outcome-label set or the `intake` capability spec.

## Decisions

### 1. Dependency readiness gates claim, not just storage

Today's `ClaimSql` matches solely on `status = Queued AND profile_key /
image / profiles_ref`; dependency rows are stored but never consulted.
This change adds a readiness predicate: an item created `Blocked` moves
to `Queued` only when every `DependsOnWorkItemId` row is `Succeeded`; the
unblock check runs inside the same transaction that finalizes a
prerequisite's terminal status — no separate polling sweep, since the
reconciliation path already visits the row.

**Why:** matches Comuki's "LLM proposes, system disposes" model — the
state machine is the source of truth for readiness, not the caller.
**Rejected:** a periodic external "unblock sweep" — same race surface as
the reaper without the reaper's need for time-based expiry.

### 2. Run terminal status is exactly-once, decided in the store

`a6e9df17` landed activation-on-first-claim and
finalize-on-last-terminal-item as guarded `UPDATE`s. This change proves
it under concurrency: two WorkItems finishing terminal in the same
instant must resolve to exactly one Run-terminal transition. The guard
stays a single `UPDATE ... WHERE status NOT IN (terminal set)` — the row
lock is the only synchronization primitive; no in-process lock is added.

### 3. Run terminal status is immutable; retry creates the next attempt

Remove `[RunStatus.Failed] = [RunStatus.Queued]` from
`RunTransitions.table`; `Failed` joins `Succeeded`/`Cancelled` as a dead
end. `WorkItem`'s own `Failed → Queued` edge (a WorkItem-level retry path,
distinct from the reaper's lease-expiry `Running → Queued` requeue) is
**not** touched — this decision is scoped to `Run` only, matching epic
task 2.6's own wording ("Remove user-visible `Failed → Queued` **Run**
retry semantics while retaining bounded **WorkItem** lease retries").

**Why:** `add-work-management` (#89) needs a stable place to hang "create
attempt 2" — a Run that can un-terminal itself is a second, conflicting
source of truth for "did this attempt finish."
**Compatibility:** no HTTP-facing retry endpoint exists today (only
`/approve` and `/cancel`), so no external contract moves; any caller
depending on the old edge now gets the same 409 `run.terminal_state` a
cancel-on-terminal already returns.

### 4. Execution generation fences the existing WorkerId+lease model — slots stay out of scope

`decomposition.md` marks this change's `worker-runtime` delta "partial —
see #100." The concrete boundary: today ownership is purely `WorkerId` +
`LeasedBy`/`LeaseUntil` — there is no `WorkerHostId`/`SlotId`/
`ExecutionId` anywhere in `platform/src` (that vocabulary is #100's
introduction). This change adds one integer `Generation` to `Run`,
bumped whenever a non-terminal Run is cancelled or superseded. Every live
WorkItem claimed under that Run carries the Run's generation at claim
time; heartbeat/complete/fail add `AND generation = @generation` to the
existing owner+status guard in `WorkItemQueueSql.cs`. A late worker's
completion at a stale generation is rejected the same way an ownership
miss is today — 409 `work-item.not-owner`, not a new response shape.

**Rejected:** waiting for #100's full slot/ExecutionId model first — Gate
B blocks #100 too (decomposition.md Wave 4), so fencing cannot be
deferred to it; #100 generalizes this same `Generation` concept from
Run-scoped to slot-scoped later, it does not introduce it.

### 5. Cancel fences before a replacement can activate

`HostCancelRunAdapter` today transitions `Run.Status` to `Cancelled` and
journals — it does not touch live WorkItem leases. Cancel/supersede now:
(a) transitions the Run, (b) bumps `Generation`, (c) marks every live
(`Running`) WorkItem's lease as fenced, all in the same transaction. A
worker holding a pre-bump lease keeps its lease so the reaper still
reclaims it on timeout, but its next heartbeat/complete/fail fails the
generation check — cancellation is durable even if the worker never
calls back in.

### 6. Durable terminal outbox, separate from best-effort realtime

`SignalRRunEventsBroadcaster`/`IRunEventsBroadcaster` stay exactly what
they are: best-effort, at-most-once, UI-facing. This change adds the
durable contract architecture.md decision #3 already declared for every
context: `outbox_messages` + `inbox_receipts` tables (none exist
anywhere today), a `BackgroundService` dispatcher polling
`FOR UPDATE SKIP LOCKED` per architecture.md's "Background Work" section
(no Hangfire/Quartz), publishing `orchestration.run.terminated.v1` (and
`.started.v1`/`.cancelled.v1`) once a Run reaches a terminal status. The
existing `RunEvent` journal is unchanged — it remains the audit timeline;
the outbox is a new row written in the same transaction as the terminal
`RunEvent`, not a repurposing of it.

**Ownership:** lands in `Comuki.Engine.Orchestration.Infrastructure` for
this change (the Orchestration context's own outbox); extracting a
shared `Comuki.Shared.Messaging` package for other contexts to reuse the
same table/dispatcher shape is left to whichever child change needs the
second consumer — architecture.md names outbox/inbox per-context, not
centralized, so this is not a blocking decision.

### 7. Event-contract compatibility gate + breaking-deployment runbook

Per architecture.md's Integration Event Contract: types are
`<context>.<aggregate>.<past-tense>.v<major>`, evolution within a major
is additive-only, a breaking change ships a new major name. This change
adds an automated additive-only contract test for `orchestration.run.*.v1`
(new optional fields only — no field removal/rename/type change) plus
the runbook in the Migration Plan below.

### 8. Intake admission race closes through the same outbox/inbox

`IntakeRunLauncher.LaunchAsync` is a direct synchronous call today; a
crash between Intake's commit and Run creation can orphan the ticket
without a Run, and a retried delivery can double-launch. This change
gives the admission call a stable message id and an inbox dedupe check
on the Orchestration side before a Run is created, reusing decision #6's
outbox/inbox shape rather than inventing a second messaging mechanism for
one call site. `intake`'s own capability spec (outcome labels, admission
rules) is unchanged — this is an Orchestration-side "Run is created at
most once per admission message id" guarantee, which is why the
requirement lives in `runs/spec.md` (matching the existing "Webhook
delivery outcome labels" requirement, which already lives in
`runs/spec.md` today rather than in `intake/spec.md`), not a new
`intake` capability delta. `hard-rename-intake-to-integrations` (#88)
lands after this change per the wave plan, so this decision and its
delta reference "Intake"/"admission," not "Integrations."

## Risks / Trade-offs

- Removing `Failed → Queued` is an internal breaking change to
  `RunTransitions`; any test asserting the old edge must be updated in
  the same change. No external HTTP contract moves (see decision #3).
- The outbox dispatcher adds a second `BackgroundService` polling
  `FOR UPDATE SKIP LOCKED` on the same schema as the existing lease
  reaper — interval/lease-timeout defaults need tuning against the
  reaper's existing 30s sweep to avoid lock contention (see Open
  Questions; not a blocking unknown).
- Generation fencing adds a column to `runs` and `work_items` — a
  migration on live tables; sequenced after the claim/reconciliation
  workstreams land so the migration doesn't chase two sets of in-flight
  edits to the same queue SQL file (see tasks.md dependency chain).
- The current `openspec/specs/runs/spec.md`'s trailing "ADAPTER Notes"
  section ("no dependency-scheduling behavior is implemented yet") goes
  stale once this change's dependency-gated claim lands; flag it for
  removal when this change is archived (the ADDED/MODIFIED Requirements
  delta format does not address free-text sections outside
  `## Requirements`, so this cannot be automated by the archive step).

## Migration Plan

Ordered; each step lands with its own test evidence before the next
starts (tasks.md's workstreams map onto these steps):

1. Dependency-gated claim — additive: `Blocked` items simply stay
   `Blocked` longer; no compatibility break for callers already
   tolerating `Blocked`.
2. Terminal reconciliation concurrency proof — no schema change.
3. Remove `Failed → Queued` — internal transition-table edit; audit any
   test asserting the old edge.
4. Add `Generation` column to `runs`/`work_items` + WorkItem-side
   fencing on heartbeat/complete/fail — migration; backfill existing
   non-terminal Runs to generation 1.
5. Wire cancel/supersede to bump generation and fence live WorkItems.
6. Outbox/inbox tables + dispatcher — additive; nothing consumes it yet.
7. Wire Run terminal reconciliation to publish through the outbox from
   step 6; keep the SignalR broadcast unchanged and parallel.
8. Event-contract compatibility gate + runbook (test/process only).
9. Intake admission moved onto the outbox/inbox from step 6.
10. Crown scenario proves steps 1–9 together.

**Breaking-deployment runbook** (for any future `.v1` → `.v2` on
`orchestration.run.*`, referenced by the event-contract compatibility
gate in step 8):
1. Deploy consumers that understand both `.v1` and `.v2`.
2. Switch the publisher to `.v2` (or temporarily dual-publish).
3. Drain/reconcile the outbox backlog and any dead-letters still carrying
   `.v1`.
4. Confirm no consumer watermark is still reading `.v1` before removing
   it — never remove a type while a mixed-generation consumer fleet is
   still deployed.

**Rollback:** steps 1–3 and 6–8 are independently revertible (no
external contract exposed). Step 4's migration is additive (new column,
default generation 1), so rollback only requires reverting the fencing
*checks*, not the column. Step 9's rollback requires draining Intake's
outbox queue before reverting to the synchronous admission call.

## Open Questions

No product or architecture branch remains unresolved — these are
interchangeable adapters/defaults per `add-mission-cowork/design.md`'s
own convention, tunable during delivery without changing this design:

- Outbox dispatcher poll interval and lease/visibility-timeout defaults
  (tune against the reaper's existing 30s sweep to avoid contention).
- Whether the Orchestration-local outbox implementation is extracted
  into a shared `Comuki.Shared.Messaging` package now, or on first reuse
  by another context — deferred to whichever child change needs the
  second consumer.
