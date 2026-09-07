## Context

See proposal.md for motivation. Today the engine ships a `RunStatus
.Escalated` value (one of seven) and a `RunTransitions` table that
permits `Escalated → {Running, Failed, Cancelled}` but has no scheduled
actor that drives any of those edges. The lease reaper drives
work-item-level transitions; the budget gate drives run-level
cancellation. Nothing in v1 ships a *passive* ratchet on Escalated.

Operators who escalate a run today rely on a human to either re-queue it
(via `POST /api/v1/runs/{id}/approve` → Running) or cancel/fail it.
If the human does not act, the row sits in Escalated forever and the
run timeline never closes. This blocks: (a) the FE "Needs you" card
(`docs/product/comuki-fe-requirements.md` §1), which surfaces escalated
runs, (b) the cost dashboard's terminal-state rollup, and (c) any future
"autonomy classes" UI that groups runs by their last-seen status.

## Goals / Non-Goals

**Goals:**

- A scheduled sweeper that closes the gap between human-acted and
  forever-stuck escalated runs by transitioning Escalated → Cancelled
  after a configurable idle window.
- One journal event per auto-archive, so the timeline shows the
  system-stamped transition with the age at the moment of the sweep.
- Same wiring shape as `LeaseReaperWorker`: `BackgroundService` +
  `IServiceScopeFactory` + `ISubjectScopeAccessor.AsSystem` + bounded
  `IOptions<T>` with `ValidateOnStart`.
- Zero schema change. Reuses the existing `run_events` table and the
  `Escalated → Cancelled` edge in `RunTransitions`.

**Non-Goals:**

- An autonomy *upgrade* ratchet (auto-approve, auto-pass). That is the
  next sub-slice after this lands and works the same way: a different
  sweep target, a different transition, a different journal type.
- Confidence-based skipping (Option B in the brief). Needs the Intake
  decision store, which is post-v1.
- Notification fan-out. The journal entry + structured log is the
  consumer seam for v1.x; chat/tracker fan-out comes later.
- Multi-replica safety. With one Host replica (v1), the system-scope
  query filter sees every Escalated row and the sweeper holds a fresh
  DbContext per pass, so the races do not exist. Two replicas racing
  the same row is out of scope until Redis fencing (#11) lands.

## Decisions

### 1. Sweeper shape mirrors LeaseReaper

**Choice:** `EscalationTimeoutSweeper(OrchestrationDbContext, TimeProvider,
IOptions<EscalationTimeoutOptions>)` with one public
`SweepAsync(CancellationToken) → EscalationTimeoutSwept` that runs the
query, applies transitions, journals events, and `SaveChangesAsync` in
one call. `EscalationTimeoutWorker : BackgroundService` owns the loop,
the scope and the `AsSystem` token — identical shape to
`LeaseReaperWorker`.

**Why:** the host already owns three `BackgroundService`-driven
dispatchers (lease reaper, scheduled dispatcher, run artifact packager).
The pattern is proven; deviating adds review surface for no gain.

**Rejected:** introducing a port abstraction (`IEscalatedRunQuery`) and an
Application-layer sweeper for unit-testability. The sweeper is pure EF
over `Runs`; the integration test in `Comuki.Host.Integration.Runs`
already exercises it against Testcontainers Postgres. Splitting would
double the surface for no behavioral change.

### 2. Cutoff is `UpdatedAt`, not a new column

**Choice:** `WHERE status = 'escalated' AND updated_at < @cutoff`. The
existing `runs.updated_at` column tracks the last status change, which
for Escalated rows is the moment the run entered Escalated (the
escalating transition also calls `Run.TransitionTo`, which bumps
`UpdatedAt`).

**Rejected:** adding `escalated_at timestamptz null` column. Reuse wins;
the sweep logic is identical, the migration cost is zero, and the
payload carries the computed `ageSeconds` so consumers do not have to
recompute it from `updated_at`.

### 3. Escalated → Cancelled, not Escalated → Failed

**Choice:** the sweeper transitions `Escalated → Cancelled` (terminal
"archived by system"). Failed keeps its existing semantic (an
exception happened inside the run), and the Escalated transition
table already permits it as an alternative — but Cancelled is the
right call here because the timeout is *human inaction*, not a system
exception.

**Rejected:** Escalated → Failed. Semantically wrong; would mix
cancellation and failure in dashboards.

### 4. One journal event per row, separate event type

**Choice:** a new `RunEventTypes.RunEscalationTimeout =
"run.escalation_timeout"` constant. Payload:
`{ runId, from: "escalated", to: "cancelled", ageSeconds }`. The
existing `run.status_changed` journal type is *not* emitted (mirrors
`WorkItemLeaseExpired` — the reaper-driven transition is its own
event family).

**Why:** the audit trail should make it obvious which terminal
transitions were operator/system vs human. A downstream consumer
(filter, alert) can join on `run.status_changed` *and* the explicit
`run.escalation_timeout` to reconstruct the full picture.

### 5. Options are bounded, validated on start

**Choice:** `EscalationTimeout` (`TimeSpan`, range `00:05:00`–`1.00:00:00`,
default `01:00:00`), `SweepInterval` (`TimeSpan`, range `00:00:05`–
`00:05:00`, default `00:00:15`). `ValidateDataAnnotations() +
ValidateOnStart()`.

**Why:** same shape as `LeaseOptions`. Operators tune both knobs in
`appsettings.json` under `Orchestration:EscalationTimeout`; an invalid
value fails the host at startup, not on the first sweep.

### 6. No new permission

**Choice:** the sweeper is invisible to operators. There is no
endpoint to enable/disable it; only the `Orchestration:EscalationTimeout
:Enabled` config knob (default `true`) gates it for ops hotfixes.

**Why:** adding `autonomy:write` is overkill for a single boolean.

## Risks / Trade-offs

- **Multi-replica races** — the sweeper is one-Host-only today; the
  DbContext scope is fresh per pass, so two passes within one replica
  do not race. Two replicas racing the same row would both transition
  Escalated → Cancelled and both journal — idempotent for the run
  status, but the journal would carry a duplicate audit row. Mitigation
  (when #11 Redis lands): `FOR UPDATE SKIP LOCKED` sweep, or optimistic
  concurrency token on `Run.UpdatedAt`. **Not in this slice.**
- **Aggressive timeout misconfig** — an operator who sets
  `EscalationTimeout=00:05:00` will cancel runs that a human is still
  reviewing. The 5m lower bound prevents accidental 30-second cancels.
  The journal entry is the audit trail; the run is recoverable from the
  timeline but the work items underneath stay in their last state.
- **No rollback** — once Cancelled, the run is terminal and the
  transition table forbids Cancelled → anything. Operators who
  misconfigure must increase the timeout *before* the next sweep. The
  config knob is what mitigates this.
- **Time drift** — `cutoff = now - EscalationTimeout` uses the injected
  `TimeProvider`, not `DateTimeOffset.UtcNow`, so tests can fast-forward
  the clock without sleeping. The sweep's "now" and the seed's "old
  UpdatedAt" are both TimeProvider-driven in the integration test.

## Migration Plan

No schema change. The change is purely Application/Infrastructure.

1. Add `RunEventTypes.RunEscalationTimeout` constant (Domain).
2. Add `EscalationTimeoutOptions`, sweeper, hosted worker, payload
   helper, and DI wiring (Infrastructure).
3. Integration test in `Comuki.Host.Integration.Runs` that seeds an
   Escalated row with `UpdatedAt` older than the configured timeout,
   runs one sweep, asserts the row is Cancelled and one journal row
   was appended with the expected payload.

Rollback: `Orchestration:EscalationTimeout:Enabled=false` stops the
worker (the bound boolean gates `AddHostedService`). The sweeper and
options stay; no destructive cleanup.

## Open Questions

- **Should the ratchet be configurable per project?** — v1.1 is
  global. Per-project override is a later slice if/when Projects gain an
  autonomy column.
- **Should the journal entry include the actor ("system")?** — implied
  by event type (`run.escalation_timeout` is unambiguously system).
  Skipping a redundant `actor` field keeps the payload small.
- **Should the FE surface auto-archived runs differently?** — the
  Cancelled status with a `run.escalation_timeout` journal entry is
  the data; the FE surfaces it as Cancelled today. A "auto-archived"
  badge is a Dashboard follow-up, not this slice.