## 1. Task domain + state machine (epic 3.1)

File area: `platform/src/modules/Work/Comuki.Modules.Work.Domain/**` only.

- [ ] 1.1 Add `Comuki.Modules.Work.Domain.csproj` to `comuki.slnx` under solution folder `platform/src/modules/Work`; verify `dotnet sln comuki.slnx list` shows the new project and `dotnet build comuki.slnx -c Debug` compiles it standalone.
- [ ] 1.2 Add the `WorkTask` aggregate with status enum (`Draft | Ready | Active | Blocked | Resolved | Cancelled`), resolution-outcome enum (`Succeeded | Waived | Replaced | Failed`), immutable attempt ledger, versioned brief, source-link collection, dependency graph, and responsible-actor metadata; verify unit tests cover every legal transition and at least one rejection per illegal edge.
- [ ] 1.3 Implement the WorkTaskStatusMachine as the single table-driven transition map (same shape as `Comuki.Engine.Orchestration.Application.WorkItemStatusMachine`); verify an illegal transition throws and there is no other status-mutation path.
- [ ] 1.4 Implement the one-active-Run invariant on the aggregate guard: an attempt append requires `WorkTask.status ∈ { Ready, Blocked }` AND `WorkTask.activeAttemptId == null`; verify two concurrent `AppendAttempt` calls on the same Task produce exactly one accepted attempt and one rejected (409 on the application surface).
- [ ] 1.5 Add value objects: `WorkTaskId` (typed Guid), `WorkTaskSourceRef` (kind, external id, opaque display name), `WorkTaskDependency` (kind: blocks | relates-to, target TaskId, redacted-stub flag), `WorkTaskAttemptOrdinal` (positive int, monotonic per Task); verify each compiles with `init`-only properties and a private ctor.
- [ ] 1.6 Add the `TaskVisibilityTransition` invariant: a standalone Task uses project RBAC; on first Mission attachment, the Task and prior/future Runs/artifacts immediately require Mission access; verify the transition is one-way (no return to standalone after Mission attachment).

**Acceptance criteria:** the Work.Domain project compiles, every legal/illegal transition has a unit test, and `dotnet build comuki.slnx -c Debug` stays green. This workstream is independently mergeable; later workstreams depend on the WorkTask aggregate shape defined here.

## 2. Work application layer + Orchestration outbox/inbox integration (epic 3.2)

File area: `platform/src/modules/Work/Comuki.Modules.Work.Application/**` + the Work-facing inbox/outbox adapter ports (NOT Orchestration's own outbox implementation — that is #87's file area).

- [ ] 2.1 Add `Comuki.Modules.Work.Application.csproj` to `comuki.slnx`; reference `Comuki.Modules.Work.Domain` and `Comuki.Shared.Messaging`; verify the build graph stays acyclic.
- [ ] 2.2 Implement the `Work.AdmitTask` command handler (idempotent on inbound id; returns existing Task identity if the inbound id is already bound to one); verify a webhook replay and a claim retry both return the same `WorkTaskId`.
- [ ] 2.3 Implement the `Work.DispatchRun` command: emits the `Orchestration.StartRun` command into the Work outbox in the same transaction as the WorkTask state change (`→ Active`); verify the outbox row carries the expected Run generation fence for #87.
- [ ] 2.4 Implement the `Work.CancelAttempt` command: emits `Orchestration.CancelRun` into the outbox, fenced by attempt ordinal; verify the WorkTask's `activeAttemptId` is cleared in the same transaction.
- [ ] 2.5 Implement the `Work.IngestRunTerminal` inbox consumer for `orchestration.run.terminated.v1` and `orchestration.run.cancelled.v1`; on terminal, advance the WorkTask per the resolution outcome (set attempt terminal, move to `→ Resolved` on final success, move to `→ Blocked` on exhaustion); verify a duplicate terminal event for the same attempt is no-op'd via inbox dedupe.
- [ ] 2.6 Implement the `WorkTaskId` projection emitted as `work.task.created.v1`, `work.task.readied.v1`, `work.task.attempt-requested.v1`, `work.task.blocked.v1`, `work.task.resolved.v1`, `work.task.cancelled.v1` per the umbrella's `<context>.<aggregate>.<past-tense>.v<major>` event-naming rule; verify the outbox rows carry stable envelope ids.
- [ ] 2.7 Enforce the one-active-Run invariant in `Work.DispatchRun`: reject with `409 work.task.run-active` when an attempt is already active; verify a concurrent `DispatchRun` against the same Task produces one accepted emission and one rejected.

**Acceptance criteria:** the application layer is the only place that emits Work outbox commands or applies Work inbox events; every command is idempotent on its key; the one-active-Run invariant is enforced end to end.

## 3. Admission integration + compatibility projections (epic 3.3, 3.7)

File area: `platform/src/modules/Work/Comuki.Modules.Work.Infrastructure/**` + the Host composition/API adapter layer for Task read/write surfaces (`platform/src/host/Comuki.Host/Api/Work/**` per `add-mission-cowork/architecture.md` target layout).

- [ ] 3.1 Add `Comuki.Modules.Work.Infrastructure.csproj` to `comuki.slnx`; reference `Comuki.Modules.Work.Domain`, `Comuki.Modules.Work.Application`, and `Comuki.Engine.Orchestration` (for the Outbox/Inbox abstractions introduced by #87); verify the dependency graph stays one-way.
- [ ] 3.2 Add `WorkDbContext` with `__comuki_work` schema, the `WorkTask`, `WorkTaskAttempt`, `WorkTaskSourceRef`, `WorkTaskDependency`, `WorkTaskAssignment`, `WorkTaskCompletionPolicy` entity sets, and explicit `bigint Version` optimistic concurrency on `WorkTask` per architecture.md §Persistence; verify the first EF migration lands cleanly via `dotnet ef migrations add WorkInit`.
- [ ] 3.3 Register `WorkDbContext` and `AddWorkModule` in `Comuki.Host/HostComposer.cs`; verify `dotnet build comuki.slnx -c Debug` and the WorkDbContext migrates at boot when `work.management.admission.enabled = true`.
- [ ] 3.4 Implement the Integrations-admission hook: when `integration.inbound.admitted.v1` arrives in Work's inbox, dispatch `Work.AdmitTask` idempotently; verify a replayed `integration.inbound.admitted.v1` returns the same `WorkTaskId` and never creates a second Task.
- [ ] 3.5 Implement the `WorkTaskRunView` projection: a Host/API read endpoint that returns the shape-compatible Run record for `GET /api/v1/work/tasks/{taskId}/run-view`, projecting the current or most-recent attempt; verify a snapshot test asserts the response shape matches today's `GET /api/v1/runs/{runId}` for the same Run.
- [ ] 3.6 Implement the `WorkTaskSummary` Host/API read (`GET /api/v1/work/tasks`, `GET /api/v1/work/tasks/{taskId}`) including `currentAttempt`, `attemptHistory`, `dependencies`, `sourceRefs`, `responsibleActors`, `completionPolicy`; verify the projection is assembly-tested to omit private Mission content (redacted stubs only).
- [ ] 3.7 Add `work.task.{create, revise, assign, link-source, relate, dispatch, cancel-attempt, resolve}` and `work.tasks.{query, read, attempts, dependencies}` capability descriptors (contracts only — full Broker exposure is #90's work); verify the descriptors compile and are registered in the Work.Application assembly.

**Acceptance criteria:** the Work Infrastructure project compiles, the WorkDbContext migration runs, the admission idempotency test passes for both webhook replay and claim retry, and the `WorkTaskRunView` snapshot test stays green.

## 4. Tracker sync migration (epic 3.4)

File area: the sync-bridge integration between Work resolution events and the (post-#88) Integrations outbound sync path; lives under `Comuki.Modules.Work.Infrastructure/Sync/` plus an adapter registration in `Comuki.Host/Composition/Integrations/`.

- [ ] 4.1 Implement the Work→Integrations sync-bridge worker: subscribes to `work.task.resolved.v1` and `work.task.cancelled.v1`, emits one deduped outbound sync job per Task id into the Integrations outbound path; verify a Task that resolves once after multiple attempts produces exactly one final sync outcome.
- [ ] 4.2 Implement the dedupe key: `work.task.{taskId}:resolved:{taskVersion}` for resolved events and `work.task.{taskId}:cancelled:{taskVersion}` for cancelled events; verify replays of the same Task resolution no-op via inbox dedupe and the sync job is emitted at most once.
- [ ] 4.3 Replace the existing `RunStatusBridgeComukiWorker` subscription with the Work resolution event subscription (gated by the `work.management.sync.enabled` feature flag); verify the old subscription stays active while the flag is off and is removed when the flag flips on.
- [ ] 4.4 Snapshot-test the bridge end to end: emit a fake `work.task.resolved.v1`, assert exactly one outbound sync job row per Task; emit a second fake with the same Task id + version, assert zero new rows.

**Acceptance criteria:** the sync-bridge emits one final sync outcome per Task regardless of attempt history; the old `RunStatusBridgeComukiWorker` is removed cleanly when the feature flag flips on.

## 5. Blocked / attempt-exhaustion Decisions (epic 3.5)

File area: `Comuki.Modules.Work.Application/Decisions/` — deterministic Decision handlers, no LLM-direct transition path.

- [ ] 5.1 Implement `RetryDecision` handler: on `Blocked` from exhaustion, create attempt N+1, fence the previous attempt, transition Task `Blocked → Ready → Active` via `Work.DispatchRun`; verify the retry Decision creates a new attempt ordinal and never resurrects the previous one.
- [ ] 5.2 Implement `ReplacementDecision` handler: on `Blocked` from exhaustion with a replaced brief/source, mark the previous Task `→ Resolved` with outcome `Replaced`, create a new Task bound to the inbound id (the inbound id's authoritative Task becomes the new one; the legacy Task is preserved as a historical reference for compatibility projection); verify the inbound id's `WorkTaskId` moves to the new Task exactly once and the old Task is read-only after replacement.
- [ ] 5.3 Implement `WaiverDecision` handler: on `Blocked` from exhaustion with explicit human waiver, transition Task `Blocked → Resolved` with outcome `Waived`; verify the waiver Decision requires the responsible human actor's proposal and a distinct-human approval per `add-mission-cowork/architecture.md` Identity axes.
- [ ] 5.4 Implement `FailedResolutionDecision` handler: on `Blocked` from exhaustion with no further action possible, transition Task `Blocked → Resolved` with outcome `Failed`; verify the failed resolution Decision is also the only path to a `Failed` Task outcome (a terminal `Failed` Run alone does not resolve the Task).
- [ ] 5.5 Implement `CancellationDecision` handler: from any non-terminal status (`Draft`, `Ready`, `Active`, `Blocked`), transition Task `→ Cancelled`; verify cancellation is irreversible and clears the active attempt in the same transaction.
- [ ] 5.6 Enforce "no LLM direct transition": every Decision handler is invoked only through the `Work.Decide` command, which accepts Decisions raised by Brain proposals (`BrainAssisted`) or humans (`HumanReviewer`); verify Brain never holds a token that calls `Work.Resolution` directly.

**Acceptance criteria:** every Blocked Task advances to one of `Active` (retry), `Resolved` (waiver / failed), `Cancelled`, or is replaced by a new Task; every transition is decided by a deterministic handler; no LLM path bypasses the machine.

## 6. Backfill migration (epic 3.6)

File area: `Comuki.Migrator` + `Comuki.Modules.Work.Infrastructure/Migration/` backfill scripts.

- [ ] 6.1 Add `Comuki.Migrator` registration for the Work backfill job: a one-shot script that walks `IncomingTicket`, `Run`, and `sync_job` rows in the legacy schemas and produces per-row outcomes per the umbrella cutover matrix; verify the job is idempotent (every processed row carries a `task_backfill_marker` and retried runs no-op).
- [ ] 6.2 Implement the "Pending `IncomingTicket`" path: on first claim after the migration, the claim goes through `Work.AdmitTask` and creates a fresh Task; the migration pre-creates a Task per pending ticket so the inbound id maps to a Task id at migration time; verify pending ticket rows acquire a non-null `WorkTaskId` after migration.
- [ ] 6.3 Implement the "Claimed + active Run" path: every Claimed `IncomingTicket` with at least one active Run becomes attempt 1 of a freshly-created Task; the Task's `activeAttemptId` is set to the legacy Run id; verify the migration result is keyed on inbound id and a retried migration leaves attempt 1 alone.
- [ ] 6.4 Implement the "Terminal Run not yet synced" path: every terminal Run with a still-pending `sync_job` row becomes one Task outcome and emits one deduped sync job; verify the reconciliation is idempotent (a retried migration produces zero new sync jobs after the first successful pass).
- [ ] 6.5 Implement the "Historical terminal Run" path: historical terminal Runs without a pending sync job are recorded as historical references for the compatibility projection only — they do NOT become Tasks; verify the migration leaves them with `task_backfill_marker = 'historical-no-task'` and they are surfaced by `WorkTaskRunView` as legacy records.
- [ ] 6.6 Implement the "Pending/failed sync job" path: every pending or failed `sync_job` row is drained or translated before the Task-based bridge is enabled; verify the migrator refuses to flip the `work.management.sync.enabled` feature flag until the sync_job queue is empty or all rows carry a `drain_marker`.
- [ ] 6.7 Cover every row of the cutover matrix in `tests/integration/Comuki.Host.WorkMigration.Integration/MigrationMatrixShould.cs` against a Testcontainers Postgres fixture seeded with one row per matrix cell; verify each cell yields the expected Task / Run / sync_job post-migration state.

**Acceptance criteria:** the migrator runs once per environment, is fully idempotent, and the integration test covers every cutover-matrix row.

## 7. Extended coordination: sources, actors, cross-Mission edges (epic 19.1, 19.2, 19.3)

File area: Work Domain/Application additions for multi-source refs + primary-change Decision, responsible-actor metadata, cross-Mission blocking-edge + redacted-stub projection.

- [ ] 7.1 Implement multi-source refs on `WorkTask`: a Task MAY aggregate several `WorkTaskSourceRef`s, exactly one is the primary; verify the primary is enforced at the aggregate guard and a Task with zero sources is rejected.
- [ ] 7.2 Implement `ChangePrimarySourceDecision`: an authorized Decision changes the primary source; the previous primary is kept as a related source with a link note; related sources receive accepted key Decisions and terminal summaries but not intermediate status events; verify a Task with three sources emits the key-decision summary to all three and the lifecycle sync only to the new primary.
- [ ] 7.3 Implement responsible-actor metadata: `WorkTaskAssignment` rows carrying human/service actor references, optional `capacityHint` (for service actors) and `proposalRequired` (for human actors); verify assignment does NOT grant authorization (the `RequiresPermission` pipeline is unchanged) and that the assignment is audited.
- [ ] 7.4 Implement Brain assignment rules: a `BrainAssisted` Decision may auto-assign eligible service actors under the configured autonomy/policy; verify auto-assignment honors the actor's grants + capacity (no over-allocation) and a failed policy check emits a `work.task.assignment-rejected.v1` event.
- [ ] 7.5 Implement human-assignment proposals: a human assignment is a Decision proposal that requires a distinct-human approval before it becomes effective; verify a human self-assignment without approval is rejected and the proposal is logged for audit.
- [ ] 7.6 Implement cross-Mission blocking edges: a `WorkTaskDependency` with `crossMission = true` requires the creator to have access to both Missions; verify the read projection emits a redacted dependency stub (status class + `request-access` action) to participants without access to the upstream Mission.
- [ ] 7.7 Implement dual-access creation for cross-Mission edges: the creation command validates creator access against both Missions atomically (via the Mission object-policy seam owned by `add-minimal-missions`); verify a creator with access to only one side receives a `403 work.dependency.cross-mission.denied` and no edge row is created.

**Acceptance criteria:** every extended-coordination requirement is satisfied with a unit test for the aggregate guard plus an integration test for the read projection; the redacted dependency stub never leaks upstream title or artifact refs.

## 8. Task completion policy (epic 19.5)

File area: `Comuki.Modules.Work.Domain/Completion/` (the policy + evidence contract types) + `Comuki.Modules.Work.Application/Completion/` (the policy handlers).

- [ ] 8.1 Implement the `CompletionPolicy` enum + value object: `Deterministic`, `BrainAssisted`, `HumanReviewer`; a policy carries an `EvidenceContract` (typed list of required evidence kinds: `RunReport`, `ArtifactPointer`, `VerificationRunReport`, `HumanAttestation`); verify a Task with no `CompletionPolicy` is rejected at creation.
- [ ] 8.2 Implement the `Work.Resolve` command: accepts a Decision (auto, Brain, or human) carrying the proposed outcome; the command checks the `EvidenceContract` (every required kind is present) and the actor/credential/authorization-subject axes (reviewer separation: the same actor who authored a replacement Decision cannot approve the resulting Task resolution); verify a Resolve without all required evidence kinds is rejected with `422 work.completion.evidence-incomplete`.
- [ ] 8.3 Implement reviewer separation at the aggregate guard: the `Resolve` command's actor identity is checked against the prior attempt's authoring actor identity; verify a same-actor Resolve is rejected with `409 work.completion.reviewer-separation` and the audit row records the rejection.
- [ ] 8.4 Implement `RepairProposal`: a successful Run followed by a failed verification produces a `WorkTaskRepairProposal` Decision (Brain or human) that blocks the Task without rewriting the successful Run's terminal status; verify the successful Run's `run_events` journal stays immutable and the Task is in `Blocked` awaiting the Decision.
- [ ] 8.5 Implement the `WorkTaskCompletionPolicy` persistence: per-Task policy with versioned `EvidenceContract` rows; verify a policy change creates a new version (no silent mutation of historical evidence requirements) and `Resolve` always reads the policy version active at the time of the attempt's terminal, not the current version.

**Acceptance criteria:** every Resolve requires a complete `EvidenceContract`; reviewer separation is enforced at the aggregate guard; a failed verification after a successful Run produces a `Blocked` Task and a `RepairProposal` opportunity without mutating the successful Run's terminal state.

## 9. Gates

Run before merging any workstream and as the final Definition of Done for this change:

- [ ] 9.1 `dotnet build comuki.slnx -c Debug` — compile + analyzers + format-verify, 0 warnings, 0 errors; verify the build exits 0.
- [ ] 9.2 `dotnet format comuki.slnx --verify-no-changes --severity warn` — format gate stays clean; verify no drift.
- [ ] 9.3 `dotnet run --project tests/Comuki.Architecture.Tests -c Debug --no-build` — architecture tests pass (no Work module referencing a sibling module's internals, no domain project depending on EF/ASP.NET/HttpClient); verify exit 0.
- [ ] 9.4 New unit project `tests/unit/Comuki.Modules.Work.Unit` (xUnit v2, named to match existing `tests/unit/Comuki.Modules.*.Unit` siblings) — `dotnet test tests/unit/Comuki.Modules.Work.Unit --no-build` passes; verify every Task state-machine transition and Decision handler has a unit test.
- [ ] 9.5 New integration project `tests/integration/Comuki.Host.WorkMigration.Integration` (xUnit v2 + Testcontainers Postgres, per `tests/integration/Comuki.Host.*.Integration` sibling convention) — `dotnet run --project tests/integration/Comuki.Host.WorkMigration.Integration -c Debug --no-build` passes; verify every cutover-matrix row has an integration test.
- [ ] 9.6 T2 scenario harness via `tests/tools/Comuki.AgentTest.Runner` (per `add-agentic-test-contour/specs/agentic-testing/spec.md` §"T2b") — `bun run tools:agent-test -- scenario work/end-to-end.yaml --mode fake` passes; verify admission → Task → Run-attempt → resolution end to end with no live model call.

## 10. Test Plan

The decomposition-table test-tier cell for this capability is "T0 unit
(legal/illegal Task transitions) + T1 integration (outbox/inbox dispatch,
backfill migration per task 3.6 against Testcontainers fixtures for *every*
existing state) + T2 scenario (`tests/tools/Comuki.AgentTest.Runner` + fake
model: admission → Task → Run-attempt loop end to end)".

- [ ] 10.1 **T0 — unit.** `Comuki.Modules.Work.Unit`: every legal and illegal `WorkTask` transition is covered by a parametrized `[Theory]` test (per WorkTaskStatusMachine); the resolution-outcome enum is tested for one transition per outcome; the reviewer-separation guard is tested for the same-actor rejection case; verify `dotnet test` exits 0.
- [ ] 10.2 **T1 — integration.** A shared `Testcontainers Postgres` fixture in `Comuki.Host.WorkMigration.Integration` per `add-agentic-test-contour/specs/agentic-testing/spec.md` §"T1 integration runs on one shared, reset database": one collection fixture, all Work module schemas migrated once, `Respawn` reset between tests, no ad hoc `PostgreSqlBuilder` per test class; verify the outbox/inbox dispatch integration test runs on this fixture and the backfill migration tests cover all 6 cutover-matrix rows.
- [ ] 10.3 **T2 — agent-loop scenario.** A `tests/tools/Comuki.AgentTest.Runner/scenarios/work/end-to-end.yaml` scenario in `fake` mode per `add-agentic-test-contour/specs/agentic-testing/spec.md` §"One scenario format, three execution modes": a tracker webhook arrives at the post-#88 Integrations adapter, `Work.AdmitTask` creates a Task, `Work.DispatchRun` emits an `Orchestration.StartRun` command, the fake Orchestration returns a terminal event, Work resolves the Task, the sync-bridge emits one final sync job; verify the scenario passes against the fake model and the recorded cassette is redaction-clean per `add-agentic-test-contour/specs/agentic-testing/spec.md` §"Cassettes are redacted before they are written".
- [ ] 10.4 **Coverage threshold.** `tests/unit/Comuki.Modules.Work.Unit` carries a coverlet threshold of `70` line coverage per `testing-stack-and-pyramid.md`; verify the Work.Domain + Work.Application covered lines sum to ≥ 70 % line coverage on a clean run.