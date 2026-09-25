# Tasks — execution-spine-orchestration

Grouped into independently-deliverable workstreams, each sized for one
agent/MR. `Depends on` lists hard prerequisites (the dependency's
acceptance criteria must be met, not just started) — several
dependencies here are file-overlap sequencing (same SQL file touched by
two workstreams), not conceptual ordering; noted per workstream.
Workstream numbering (`N.1`, `N.2`, …) is local to each WS; the epic task
id each item closes (`add-mission-cowork/tasks.md` §2) is named in the
item text for traceability.

This change is entirely below the Task/Mission/dashboard layer, so the
compose.e2e/Playwright/AgentEval tiers do not apply — the crown proof
(WS10) uses the in-repo scenario runner (`Comuki.AgentTest.Runner`) and
fake model (`Comuki.TestFakeModel`/`Comuki.TestFakePi`) tiers only, per
the agentic test contour already landed at `tests/tools/*`.

Cross-references — do not duplicate, coordinate instead:
- `hard-rename-intake-to-integrations` (#88) renames `Intake` →
  `Integrations` after this change lands (decomposition.md Wave 2); WS9
  below touches `IntakeRunLauncher.cs` under its current name/namespace
  — #88 will move/rename it, not this change.
- `add-worker-pools-and-isolation-classes` (#100) generalizes the
  `Generation` concept this change introduces (WS4/WS5) from
  Run-scoped to per-slot `WorkerHostId`/`SlotId`/`ExecutionId`-scoped —
  do not introduce slot vocabulary here.
- `add-work-management` (#89) is the first consumer of
  `orchestration.run.*` outbox events (WS6/WS7) and of the immutable
  terminal Run contract (WS3) — coordinate the event payload shape's
  landing order if #89 starts before this change's WS7 lands.

---

## WS1 — Dependency-gated claim

**Depends on:** none. **Size:** M.
**Files:** `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Queue/WorkItemQueueSql.cs`,
`.../Infrastructure/Queue/WorkItemQueueEf.cs`,
`.../Domain/WorkItems/WorkItem.cs`,
`.../Domain/WorkItems/WorkItemDependency.cs`,
`tests/integration/Comuki.Engine.Orchestration.Integration.Queue/WorkItemQueueShould.cs` (extend).

- [x] 1.1 Add a readiness predicate to the claim path: a `Blocked`
      WorkItem is claimable only after every `work_item_dependencies`
      prerequisite reaches `Succeeded` (epic task 2.1).
- [x] 1.2 Unblock `Blocked` → `Queued` in the same transaction that
      finalizes a prerequisite's terminal status — no separate polling
      sweep.
- [x] 1.3 A prerequisite reaching `Failed`/`Cancelled` does NOT
      auto-unblock its dependent; it stays `Blocked` for plan-level
      failure policy to resolve.

**Acceptance:** an integration test (Testcontainers) proves a dependent
item cannot be claimed before its prerequisite succeeds, and is claimable
immediately after; `WorkItemQueueShould.cs` covers both the block and the
unblock transaction.

---

## WS2 — Run terminal reconciliation, exactly-once under concurrency

**Depends on:** WS1 (same `WorkItemQueueSql.cs`/`WorkItemQueueEf.cs` —
sequenced to avoid conflicting edits to the shared file, not a
conceptual dependency). **Size:** M.
**Files:** same as WS1's queue files,
`tests/integration/Comuki.Engine.Orchestration.Integration.Queue/RunJournalShould.cs` (extend).

- [x] 2.1 Prove the existing `a6e9df17` reconciliation slice
      (finalize-on-last-terminal-item) is exactly-once under
      concurrent completions — two WorkItems finishing terminal in the
      same instant must resolve to one Run-terminal transition (epic
      task 2.2).
- [x] 2.2 Confirm the guard stays a single `UPDATE ... WHERE status NOT
      IN (terminal set)` with no additional in-process lock.

**Acceptance:** a Testcontainers integration test races two concurrent
final-WorkItem completions and asserts exactly one Run terminal
transition and (once WS7 lands) exactly one terminal outbox message —
for this workstream, assert the Run-status half only.

---

## WS3 — Remove `Failed → Queued` Run retry edge

**Depends on:** none (parallel-safe with WS1 — disjoint file).
**Size:** S.
**Files:** `platform/src/engine/Comuki.Engine.Orchestration/Domain/Runs/RunTransitions.cs`,
`tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine/RunStatusMachineShould.cs` (extend).

- [ ] 3.1 Remove `[RunStatus.Failed] = [RunStatus.Queued]` from
      `RunTransitions.table`; `Failed` becomes terminal (`[]`) like
      `Succeeded`/`Cancelled` (epic task 2.6).
- [ ] 3.2 Update the stale XML-doc comments on `RunTransitions` and
      `RunStatus.Failed` that describe the old retry edge.
- [ ] 3.3 Confirm `WorkItem`'s own `Failed → Queued` edge is untouched —
      this change is scoped to `Run` only.

**Acceptance:** `RunStatusMachineShould.cs` asserts `Failed` has no legal
outgoing transitions; a grep for the old edge/comment text confirms no
leftover reference.

---

## WS4 — Execution generation: schema + WorkItem-side fencing

**Depends on:** WS1, WS2 (same queue files). **Size:** L.
**Files:** `platform/src/engine/Comuki.Engine.Orchestration/Domain/Runs/Run.cs`,
`.../Domain/WorkItems/WorkItem.cs`,
`.../Infrastructure/Queue/WorkItemQueueSql.cs`,
`.../Infrastructure/Queue/WorkItemQueueEf.cs`,
new migration under `.../Infrastructure/Migrations/`,
`platform/src/host/Comuki.Host/Workers/Api/WorkerEndpoints.cs`,
`platform/src/host/Comuki.Host/Workers/Api/ClaimedWorkItemResponse.cs`,
`platform/src/host/Comuki.Host/Workers/Api/CompleteWorkItemRequest.cs`,
`platform/src/host/Comuki.Host/Workers/Api/FailWorkItemRequest.cs`,
`tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine/WorkItemLeaseShould.cs` (extend),
`tests/integration/Comuki.Host.Integration.Workers/` (extend).

- [ ] 4.1 Add an integer `Generation` column to `runs` (default 1) and a
      `generation` column to `work_items` (the generation it was
      claimed under); new EF migration (epic task 2.3).
- [ ] 4.2 `heartbeat`/`complete`/`fail` SQL adds `AND generation =
      @generation` to the existing owner+status guard; a mismatch
      answers the existing 409 `work-item.not-owner` code — not a new
      response shape.
- [ ] 4.3 Claim response (`ClaimedWorkItemResponse`) surfaces the
      claimed generation; heartbeat/complete/fail requests carry it back.

**Acceptance:** a unit test proves the fencing decision (current vs.
stale generation) in isolation; an integration test proves a worker
completing at a stale generation is rejected 409 while completing at the
current generation still succeeds exactly as before this change.

---

## WS5 — Cancel/supersede fences live executions

**Depends on:** WS4 (needs `Generation` to exist). **Size:** M.
**Files:** `platform/src/host/Comuki.Host/Runs/HostCancelRunAdapter.cs`,
`platform/src/host/Comuki.Host/Runs/Controllers/RunsController.cs` (verify only — response codes unchanged),
`tests/integration/Comuki.Host.Integration.Runs/` (extend).

- [ ] 5.1 Cancel bumps the Run's `Generation` and fences every currently
      `Running` WorkItem under it, in the same transaction as the
      `Cancelled` status transition and journal append (epic task 2.3
      cancel half).
- [ ] 5.2 A fenced WorkItem's lease is left intact for the reaper to
      reclaim on the existing TTL/grace schedule — fencing invalidates
      authority, it does not forge a lease release.
- [ ] 5.3 Verify `POST /api/v1/runs/{runId}/cancel`'s existing 409
      (terminal run) / 404 (unknown/out-of-scope) behavior is unchanged.

**Acceptance:** an integration test cancels a Running Run with a live
WorkItem execution, then has the worker attempt heartbeat/complete at
its pre-cancel generation — the mutation is rejected and the Run/Task
outcome is unaffected by the late call.

---

## WS6 — Durable outbox/inbox infrastructure

**Depends on:** none (new files only — parallel-safe with WS1/WS3).
**Size:** L.
**Files (all new):** `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Outbox/OutboxMessage.cs`,
`.../Infrastructure/Outbox/IOutbox.cs`,
`.../Infrastructure/Outbox/OutboxDispatcher.cs`,
`.../Infrastructure/Outbox/InboxReceipt.cs`,
`.../Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`,
`.../Infrastructure/Persistence/Configurations/InboxReceiptConfiguration.cs`,
new migration under `.../Infrastructure/Migrations/`,
new `tests/integration/Comuki.Engine.Orchestration.Integration.Queue/OutboxDispatchShould.cs`
(or a new sibling `Comuki.Engine.Orchestration.Integration.Outbox` project if
cohesion favors it — document the choice made in the PR description).

- [x] 6.1 `outbox_messages`/`inbox_receipts` tables, snake_case, under
      the orchestration schema, per `add-mission-cowork/architecture.md`
      decision #3 (epic task 2.4 infra half).
- [x] 6.2 `IOutbox` write-side port (enqueue in the same transaction as
      the caller's aggregate commit); `BackgroundService` dispatcher
      polling `FOR UPDATE SKIP LOCKED`, bounded retries, visible
      dead-letter state on exhausted retries — no Hangfire/Quartz.
- [x] 6.3 Inbox dedupe by message id (`inbox_receipts`) for consumers.
- [x] 6.4 Partial index on `outbox_messages` for undispatched rows
      (`(dispatched_at, created_at)` filtered to `dispatched_at IS
      NULL`), matching the claim-path partial-index convention already
      used on `work_items`.

**Acceptance:** a Testcontainers integration test runs two dispatcher
instances concurrently against a seeded backlog and proves `FOR UPDATE
SKIP LOCKED` gives each a disjoint row set with no double-dispatch; a
poisoned message (handler always throws) ends up dead-lettered and
visible, not retried forever or silently dropped.

---

## WS7 — Wire Run terminal reconciliation to the durable outbox

**Depends on:** WS2 (terminal reconciliation must be stable), WS6
(outbox infra must exist). **Size:** M.
**Files:** `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Queue/WorkItemQueueSql.cs`,
`.../Infrastructure/Queue/WorkItemQueueEf.cs`,
`.../Domain/Journal/RunEventTypes.cs` (add event-type constants only).
**Explicitly does NOT touch:** `Comuki.Host/Realtime/Broadcasting/SignalRRunEventsBroadcaster.cs`
— the realtime broadcast stays unchanged and runs in parallel with the
new durable path.

- [ ] 7.1 Enqueue an `orchestration.run.terminated.v1` (or `.started.v1`
      / `.cancelled.v1` as applicable) outbox message in the same
      transaction as the Run's terminal-status commit (epic task 2.4
      wiring half).
- [ ] 7.2 Confirm the existing `RunEvent` journal append is unchanged —
      the outbox row is additional, not a replacement.

**Acceptance:** the WS2 concurrency test extended to also assert exactly
one outbox message is enqueued per Run termination; a process-crash
simulation (commit succeeds, broadcast step is skipped) still yields a
delivered outbox message once the dispatcher runs.

---

## WS8 — Event-contract compatibility gate + runbook verification

**Depends on:** WS7. **Size:** S.
**Files:** a new unit/contract test under
`tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine/` (or
`Comuki.Architecture.Tests` if that project already asserts contract
shapes elsewhere — check first), design.md's Migration Plan runbook
text (verify only, no edit needed from this workstream unless a gap is
found).

- [ ] 8.1 Automated test asserting `orchestration.run.*.v1` payload
      evolution stays additive-only (new optional fields; fails the
      build on a removed/retyped field) (epic task 2.4a).
- [ ] 8.2 Dry-run the design.md breaking-deployment runbook steps
      against the WS6/WS7 implementation and confirm each step is
      actually executable with the tooling that exists (dispatcher
      pause/drain, dead-letter visibility) — file a follow-up if a step
      has no real lever yet, do not silently mark it done.

**Acceptance:** the contract test fails when a field is deliberately
removed from a fixture payload (verify the negative case, not just the
happy path); the runbook dry-run notes are recorded in the PR
description.

---

## WS9 — Intake admission idempotency via outbox/inbox

**Depends on:** WS6 (needs inbox infra). **Size:** M.
**Files:** `platform/src/host/Comuki.Host/Intake/IntakeRunLauncher.cs`,
its `IRunLauncher` port declaration (find under
`platform/src/modules/Intake/Comuki.Modules.Intake.Application/Ports/Admission/`),
`tests/integration/Comuki.Host.Integration.Intake/` (extend),
`tests/integration/Comuki.Host.Integration.Runs/` (extend if a
cross-module assertion is cleaner there).

- [x] 9.1 Give the admission call a stable message id (the ticket's
      admission/claim identity) and an inbox dedupe check on the
      Orchestration side before `Run.Create`/`WorkItem.Create` run
      (epic task 2.5).
- [x] 9.2 Retried delivery of the same message id creates at most one
      Run; concurrent delivery of the same message id resolves to one
      Run with the losing caller observing the same Run id, not an
      error.
- [x] 9.3 `intake`'s own capability spec/outcome-label set is
      unchanged — this is an Orchestration-side guarantee only.

**Acceptance:** an integration test delivers the same admission message
id twice (sequential retry) and concurrently (race) and asserts exactly
one Run exists in both cases.

---

## WS10 — Crown path proof

**Depends on:** WS1–WS9 (all). **Size:** M.
**Files:** `tests/integration/Comuki.Host.Translator.Integration.PiCli/TranslatorE2EShould.cs` (extend),
`tests/integration/Comuki.EndToEnd.AgentLoop/AgentLoopScenarioShould.cs` (extend),
optionally a new scenario fixture under
`tests/fixtures/scenarios/small-repo/` (e.g.
`execution-spine-crown.scenario.yaml`) if the existing fixtures don't
already cover a multi-WorkItem dependent plan.

- [ ] 10.1 Extend `TranslatorE2EShould` and/or `AgentLoopScenarioShould`
      (using `Comuki.AgentTest.Runner` + `Comuki.TestFakeModel`/
      `Comuki.TestFakePi`, per the landed agentic test contour) to prove,
      in one run: a dependent WorkItem waits for its prerequisite
      (WS1), the Run reaches exactly one terminal status (WS2), a
      cancelled/superseded Run's late worker result is rejected (WS4/
      WS5), a terminal outbox message is delivered (WS6/WS7), and an
      admission retry does not double-launch (WS9) (epic task 2.7).
- [ ] 10.2 compose.e2e/Playwright/AgentEval tiers do not apply to this
      change (no dashboard/CLI/HTTP-facing product surface changed) —
      do not add scenarios there; note this explicitly in the PR so a
      reviewer doesn't go looking for them.

**Acceptance:** the crown scenario passes and its assertions name each
of the five invariants above individually (not one opaque green check) —
a reviewer can see which invariant a future regression broke.
