## Context

See `proposal.md` for motivation. The Work bounded context sits between the
Integrations admission path and the Orchestration engine, owning the durable
user-facing unit of work (a `WorkTask`) and the policy/ordering that decides
when and how it dispatches Runs. It does not own worker leases or queue
mechanics — those remain in `Comuki.Engine.Orchestration` per the
already-landed `work-queue` capability (`openspec/specs/work-queue/spec.md`).

### Dependency on two not-yet-authored sibling changes

This change cannot start implementation until both
`add-execution-spine-orchestration` (#87) and
`add-hard-rename-intake-to-integrations` (#88) have landed. Per the
decomposition wave plan (`add-mission-cowork/decomposition.md`), Work sits
in Wave 3 directly behind Wave 1 (spine) and Wave 2 (rename). The mechanics
this change assumes from those siblings:

- From #87: durable per-context `outbox_messages` / `inbox_receipts`
  configured by `Comuki.Shared.Messaging`, executed with `FOR UPDATE SKIP
  LOCKED`; execution generation fencing on every cancel/supersede,
  heartbeat, complete, fail; an idempotent `Orchestration.StartRun` /
  `CancelRun` command seam.
- From #88: `Comuki.Modules.Integrations.*` (the `InboundItem` aggregate,
  the `integrations` PostgreSQL schema, the
  `integration.inbound.admitted.v1` event), replacing the current
  `Comuki.Modules.Intake.*` / `IncomingTicket` / `intake` schema. This
  change's prose and spec language refers exclusively to the post-rename
  target names; nothing in this change edits the legacy names directly.

This change is authored now (spec-first) so the Work vertical slice is ready
to build the moment Wave 1 and Wave 2 land.

### Today's greenfield code state (verified facts)

- `platform/src/engine/Comuki.Engine.Orchestration/Domain/WorkItems/`
  contains `WorkItem.cs`, `WorkItemDependency.cs`,
  `WorkItemTransitions.cs`; `Application/WorkItemStatusMachine.cs`;
  `Infrastructure/Queue/WorkItemQueueSql.cs` and
  `WorkItemQueueEf.cs`. Dependency edges are **not enforced at claim time**
  today (this is #87's job).
- `platform/src/engine/Comuki.Engine.Orchestration/Domain/Runs/Run.cs` is
  the existing `Run` aggregate — the only durable goal-shaped entity
  alongside `IncomingTicket`.
- No `IOutbox` / `outbox_messages` concept exists anywhere in
  `platform/src` (fully greenfield; #87 introduces it).
- No `Generation` / execution-fencing concept exists in Orchestration yet
  (#87 introduces it).
- `platform/src/modules/Intake/` exists with `Comuki.Modules.Intake.Domain`,
  `.Application`, `.Infrastructure`, the `IncomingTicket` entity
  (`Comuki.Modules.Intake.Domain/Tickets/IncomingTicket.cs`), and tracker
  sync via `SyncJob.cs` + `RunStatusBridgeComukiWorker.cs` (inbound
  sync-back; precedent for an outbox-dispatcher pattern, not reusable
  machinery as-is). Per the umbrella, this becomes
  `Comuki.Modules.Integrations.*` after #88 lands.
- `class Task\b` = 0 hits repo-wide. `IOutbox` / `outbox_messages` = 0
  hits. `WorkTask` aggregate: 0 hits.

## Goals / Non-Goals

**Goals:**

- Make `WorkTask` the durable user-facing unit of work, independent of
  admission context, Mission membership, or current execution.
- Keep the Task state machine deterministic; Brain proposes transitions,
  the machine disposes.
- Drive Orchestration through idempotent commands with exactly-once-
  observable effects via the durable outbox/inbox seam #87 introduces.
- Provide a migration that backfills every existing-state row of the
  umbrella's cutover matrix without losing track of legacy Runs or
  duplicate-syncing the tracker.
- Keep existing Run-shaped API/dashboard/CLI consumers working during
  the transition via compatibility projections; Task is authoritative,
  Run reads are derived.

**Non-Goals (design-level; not duplicating `proposal.md` Non-goals):**

- A new user-visible retry/replacement UX surface. The brain/proposal
  Decision flow already exists; this change only adds the deterministic
  Task-side handlers that react to it.
- A bidirectional Task↔Run sync store. The Task owns the attempt ledger;
  the Run remains the Orchestration-side execution record; reconciliation
  is via idempotent `Orchestration.StartRun` command + terminal events
  consumed by Work's inbox.
- Replacing the existing `Run` aggregate's terminal Run journal
  (`run_events`) with a Task-shaped journal. `run_events` stays
  Orchestration's; Work adds its own `task_events` / outbox for
  Task-shaped facts.
- Anything that requires the Capability Broker to exist end to end. The
  `work.*` commands are specified here; their final Broker exposure is
  #90's job.

## Decisions

### 1. The Task state machine is its own bounded-context policy

```text
WorkTask status:
  Draft → Ready → Active ↔ Blocked → Resolved
   └────────────────→ Cancelled

WorkTask resolution outcome (only meaningful when status = Resolved):
  Succeeded | Waived | Replaced | Failed
```

The state machine is its own decision and is cited verbatim from
`add-mission-cowork/design.md` Decision 2. The diagram is normative: any
attempt to transition outside the diagram is rejected at the aggregate
guard; no LLM path bypasses the machine. Resolved is terminal; Cancelled
is terminal; every other transition is gated. The attempt ledger is part
of the Task (one active attempt maximum), not a separate Run-side
counter. `WorkTaskResolutionOutcome` is set exactly once, on the
`→ Resolved` edge, and is immutable thereafter.

**Rejected:** letting Brain write the status directly; making `Blocked`
auto-resolve on next attempt success (exhaustion must produce an explicit
Decision); encoding "waived" / "replaced" as ad hoc booleans on
`WorkTask`.

### 2. Work ↔ Orchestration via durable outbox/inbox, never dual writes

Adapted from `add-mission-cowork/design.md` Decision 3, narrowed to Work:

```text
Integrations admission (post #88)
  InboundItem admitted + Work.AdmitTask command in integrations outbox
       ↓ at-least-once
Work inbox (AdmitTask)
  create/find WorkTask + WorkTaskAdmitted event + TaskResolved.next = Active
       ↓ dispatch policy
Work outbox (DispatchRun)
  Orchestration.StartRun command (idempotent on RunId; expects
  existing-attempt fencing from #87)
       ↓
Orchestration terminal outbox
  orchestration.run.terminated.v1 / orchestration.run.cancelled.v1
       ↓ Work inbox
Work resolution
  WorkTask resolved (Succeeded | Waived | Replaced | Failed) or
  Blocked (on exhaustion) → WorkTaskResolved.v1 event
       ↓ downstream consumers
Integrations sync-back (epic 3.4) → tracker dedupe
Missions consumption (room stream, completion trigger)
```

The commit point belongs to the context owning the changed fact. Work
never writes to Orchestration's tables and Orchestration never writes to
Work's. Synchronous request/response is allowed for preflight reads
(`Run status?`, `slot available?`) but never as the durability
contract. The one-active-Run invariant is enforced by Work before
emitting `Work.DispatchRun` — if a Task already has an active Run, the
command is rejected; if Orchestration has a stale Run that Work's
generation fence has fenced, Work's inbox applies the
expected-state/version guard and resolves the Task accordingly.

**Rejected:** a host-composed synchronous `InboundItem → Task → Run`
pseudo-transaction; using `run_events` or the SignalR interceptor as the
outbox; ambient distributed PostgreSQL transaction across Work and
Orchestration DbContexts.

### 3. Backfill behaviour follows the umbrella cutover matrix

The umbrella `add-mission-cowork/design.md` §"Existing-state cutover
matrix" is normative for this change's backfill. Every row maps to a
deterministic backfill job in `Comuki.Migrator`:

| Existing state | Backfill behaviour |
|---|---|
| Pending `IncomingTicket` | On claim/admission, create a Task idempotently on the inbound id; the Task becomes the authoritative identity. |
| Claimed + active Run | Mandatory Task backfill; the active Run becomes attempt 1; backfill is keyed by the inbound id and is idempotent on retry. |
| Terminal Run not yet synced | Reconcile one Task outcome (`Succeeded`/`Failed`/`Cancelled`); emit one deduped sync job (`epic 3.4`). |
| Historical terminal Run | May remain legacy/unattached/read-only; surfaced via the compatibility projection for existing consumers, not promoted to a Task. |
| Pending/failed sync job | Drain or translate before enabling the Task-based bridge; one Task → at most one final sync outcome. |
| Existing `ChatSession` | Remains personal chat unchanged (out of scope here). |

The migration runs once and is idempotent: every row carries a
`task_backfill_marker` once processed, so a retried migration no-ops on
already-processed rows. The migration is gated by a project feature flag
(`work.telemetry.enabled`/`work.management.backfill.enabled`) so dev and
staging can stage the cutover independently of prod. Dev/stage data may
be reset; prod keeps dual-write compatibility projections active until
telemetry shows no legacy consumers (see `add-mission-cowork/design.md`
§Migration Plan).

**Rejected:** silent promotion of every historical Run into a Task
(privacy + identity hazard); making the cutover a one-shot irreversible
delete without rollback version (rolls back per Epoch).

### 4. Compatibility projection: existing Run reads remain Run-shaped

Existing API/dashboard/CLI consumers reason about Runs. Replacing every
such consumer at once would block the rollout of Tasks. The compatibility
projection: the Work read API exposes a `WorkTaskSummary` projection
that embeds the current Run attempt as a field (`currentAttempt`,
`attemptHistory`) and a separate `WorkTaskRunView` endpoint whose shape
matches today's `GET /api/v1/runs/{runId}` response, except it returns
404 when the Run has been superseded. Existing `GET /api/v1/runs` list
behavior is preserved at the URL level but the response includes an
authoritative `taskId` field and the underlying rows are joined through
the Task. Tracker-side code that consumed the old `Run` shape keeps
working because the projected response is shape-compatible.

**Rejected:** big-bang rename of all `runId` → `taskId` in client
contracts (would require coordinated client rollout); serving Run reads
from Orchestration's `runs` table alone (becomes stale the moment
attempts are replaced).

### 5. Completion policy is predeclared, reviewer separation enforced

A WorkTask carries a `CompletionPolicy` declared at creation:
`Deterministic` (rule-based reviewer), `BrainAssisted` (Brain review with
audited rationale), or `HumanReviewer` (named human owner, see
`add-minimal-missions` for membership). The policy references a typed
`EvidenceContract` enumerating the evidence kinds required for
resolution (e.g. `runReport`, `artifactPointer`,
`verificationRunReport`). Reviewer separation forbids the same actor
authoring and approving a resolution — verified at `Resolve` time via
the actor/credential/authorization-subject axes (architecture.md
"Identity actor axes"). A failed verification after a successful Run
blocks the Task (`→ Blocked`) and emits a `RepairProposal` Decision
opportunity for the responsible human or service; it does NOT rewrite
the successful Run's terminal status. The successful Run's
`run_events` journal stays immutable.

**Rejected:** auto-resolving a Task on a successful Run with no
verifier; reusing the Run's success status as the Task's success
without checking the `EvidenceContract`; allowing the same actor who
authored a replacement Decision to approve the resulting Task
resolution.

## Risks / Trade-offs

- **[Risk] Outbox backlog makes Work views stale** → dispatchers expose
  lag/watermarks; dead letters visible; reconciliation per
  `add-mission-cowork/design.md` Decision 12.
- **[Risk] Backfill migration races with concurrent in-flight
  admissions** → backfill is keyed by inbound id and idempotent;
  in-flight admissions either see the Task already created (and find
  it via the Work inbox) or create a fresh one (and the backfill's
  no-op marker leaves the fresh Task alone).
- **[Risk] Compatibility projection drift after task 3.7** → the
  `WorkTaskRunView` shape is captured as a snapshot test against the
  current `/api/v1/runs/{runId}` response; any drift fails the build.
- **[Risk] Completion-policy reviewer separation is bypassed** → the
  `Resolve` command enforces it at the aggregate guard, not at the API
  surface; the same actor authoring and approving throws the same
  exception as an illegal status transition.
- **[Risk] Cross-Mission blocking edges leak private content** →
  redacted stub is enforced by the read projection (architecture.md
  Module Ownership table: Work owns Task edges, Missions owns Mission
  visibility); a Task edge projection only emits `kind`, `statusClass`,
  `requestAccessAction` for inaccessible Missions, never title/Run
  detail.
- **[Trade-off] One more durable aggregate** → every `WorkTask` adds a
  row to `__comuki_work` plus outbox/inbox rows on every transition;
  this is the cost of reliable cross-context delivery and is the
  same cost every other bounded context pays.

## Migration Plan

This change lands in three sequenced phases that mirror the umbrella's
Epoch 1 ("standalone Tasks"), each behind the
`work.management.{phase}.enabled` feature flag.

**Phase A — additive Work context.** The Work bounded context, its
`WorkDbContext`, the Work outbox/inbox, and the `WorkTask` aggregate
land. No existing code path changes; no backfill runs; new admissions
that pre-create a Task before first Run are gated by
`work.management.admission.enabled` (off in prod). The T2 agent-loop
scenario tier (per `add-agentic-test-contour/specs/agentic-testing/spec.md`)
exercises this end to end against a fake model.

**Phase B — backfill + compatibility projections.** The
`Comuki.Migrator` backfill jobs run once per environment (dev, staging,
prod independently). Legacy Run reads acquire the `taskId` field;
`WorkTaskRunView` lands behind `/api/v1/work/tasks/{taskId}/run-view`.
Existing clients are unchanged at the URL level; the projected
response shape is shape-compatible and is snapshot-tested.

**Phase C — tracker sync moved to Task resolution.** The Integrations
sync-back listens to `work.task.resolved.v1` instead of
`run.terminal` (epic 3.4). One Task produces exactly one final sync
comment, deduped on `WorkTaskId`.

**Dependency statement.** Phase A cannot begin implementation until
#87 and #88 have landed. This change's spec-first authoring here
means the Work vertical slice is ready to build the moment Wave 1 and
Wave 2 land.

**Rollback.** Until Phase B has run in prod for one full release
cycle, rolling back this change re-enables the pre-Phase-A admission
path (Tasks remain durable, but the Integrations admission creates
both the Task and a `Run` directly, exactly as today). After Phase B,
rollback is destructive of the backfilled `task_backfill_marker` rows
and is not safe — see `add-mission-cowork/design.md` §Migration Plan
for the per-epoch rollback version contract.

## Open Questions

None. The remaining open items (Knowledge spec, §13 Crown verification,
worker-pool single-slot compatibility mode, CLI ownership split) are
out of scope for this change — see `add-mission-cowork/decomposition.md`
§"Decisions (user, 2026-09-24)".