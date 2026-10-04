## Context

See `proposal.md` for motivation. Today Comuki is a run-execution substrate: one ticket in, one run, one worker, one journal, one chat. The product is what the *operator* does with that substrate, and most of those verbs are missing. The eight phases of this change are the verbs. They share one substrate (the run, the journal, the worker, the host) and one edition model (Community, then paid), so they share one umbrella change.

Constraints that shape the *how*:

- `WorkerCommandHub` ships today with `Stop` / `InjectContext` / `LeaseExpired` / `Exec` plumbing and zero production callers; the kill switch (`Stop`) is wired only to the reaper, the other commands sit on the channel without live callers. Baton is the first real caller.
- `Verify` ships on master (`commit 2d7346dc`, `platform/src/modules/Verify/**`, `GenericCommandRun` `Pending → Running → Green/Red`) with `Verify:Verifier:Enabled=false`; standalone, not bound to WorkItem/Run, not terminal-gating. Coda is the first binding.
- `WorkerCommandHub` and the mutable virtual-key store are in-process, single-replica. Multi-replica coordinator is **out of scope** for the duration of this change; the design choice is recorded in D11.
- `Outbox` exists (`OutboxMessage`, `NoopOutboxPublisher`); adopting it for the automation leg is a per-leg decision, not a default.
- `WorkerFeatureGate` exists with deferred-start semantics; today no registered worker carries `[RequiresFeature]` — the registry is consumed (`BackgroundWorkersEndpoints.cs:24` reads `ComukiWorkerRegistry.Snapshot()`, and `ComukiWorkerRegistry.PartitionWorkers` is the gate-aware partitioning entry point), but every `IComukiWorker` registration on master is feature-gate-free. This change is the first to land a worker that carries `[RequiresFeature]` (`CriticSweepWorker` with `[RequiresFeature(Features.Critic)]` in Phase 4).
- The four unused `ProjectSettings` flags (`ApproveRequired` / `KnowledgeEnabled` / `VerifyEnabled` / `ProxyEnabled`) have zero runtime consumers today; the two budget-side fields (`softBudgetUsdMicros` / `hardBudgetUsdMicros`) are already consumed by `Comuki.Host/Costs/ProjectBudgetSettingsAdapter.cs`. Section phases the four bool flags.
- `null` (Community) is the only currently-registered edition on the `WorkerFeatureGate` path. The first paid-feature call-sites land with this change.
- A second runnable harness exists as `Comuki.TestFakePi`, a swap-in replacement for `PiExecutable` in tests. It is the natural second `IHarness` implementation that proves the SPI.

`add-mission-cowork` (#70) is the work substrate this change consumes. `add-multi-repo-projects` (#163) is one of the ambient contexts Section inherits. The 19-phase cowork epic remains authoritative on Task/Run/slot identity; the `WorkerCommandHub` and the `InjectContext` semantics it defines in §11 are the contract Baton delivers against. The Instrument phase declares the SPI in slot terms and ships *with* cowork 11.1 — an SPI declared before the contract lands is a contract that will move.

## Goals / Non-Goals

**Goals:**

- Make live-session steering a first-class production call path; the existing `WorkerCommandHub` plumbing earns its first non-reaper caller.
- Add an orthogonal *verification* axis to "done"; never widen the run state machine.
- Move from "config-seed virtual keys" to "mutable virtual keys with live-run propagation", budgeted by the existing `NullBudgetGate` (costs), not a parallel path.
- Make the four scope layers (orchestra / section / worker / card) first-class in settings resolution; the mutable *orchestra* layer is the first write path to global.
- Make the swarm observable *to itself* through MCP, with the same typed store/catalog the human uses.
- Make `IHarness` the abstraction, not `IPiRunner`. pi is the first implementation; `Comuki.TestFakePi` is the second test surface.
- Bring the four unused `ProjectSettings` bool flags (`ApproveRequired` / `KnowledgeEnabled` / `VerifyEnabled` / `ProxyEnabled`) onto a real consumer path (the two budget-side fields are already consumed by `ProjectBudgetSettingsAdapter`); the first paid-feature call-sites for `[RequiresFeature]`.

**Non-Goals:** *(design-level)*

- Re-modelling the run state machine. `Succeeded` is one of seven states; the verification phase is an annotation on it.
- Multi-replica coordinator for in-process state (`WorkerCommandHub`, `IVirtualKeyStore`).
- Replacing `WorkerId` with `WorkerHostId` / `SlotId` / `ExecutionId` *here*. Slot/execution identity is cowork 11.1; Instrument rides it.
- A new `WorkerRuntimeOption` enum or live-session flag on `TranslatorOptions`. The capability is a *harness* declaration, not a Translator toggle.
- A distributed scheduler / outbox-as-bus. The automation leg uses the existing `Outbox` only when a cross-context effect needs exactly-once-observable delivery; the read-model is the default.
- Any change to claim / lease / heartbeat / claim idempotency beyond what is needed to land the per-WorkItem verification record.
- Replacing the existing intake wire format. The `Automation` object reads `SourceConnection` / `RunStatusBridge` / `SyncJob`; it does not fork the schema.
- Authoring any code. The change is the *plan*, not the implementation.

## Decisions

### 1. Order is dogfooding-first; phases ship as vertical changes

```text
Baton (session) ─▶ Critic-foundation (logs/MCP) ─▶ Coda (verification axis)
                                                       │
                                  ┌────────────────────┘
                                  ▼
                       Critic-sweep (uses foundation)  Section (4-layer scope)
                                  │                       │
                                  └────────▶  Encore (automation) ◀┘
                                              │      │
                                              ▼      ▼
                                          Tuner (mutable keys) ◀── session from Baton
                                              │
                                              ▼
                                          Instrument (IHarness)
                                                  ▲
                                                  │ rides cowork 11.1
                                                  │ (WorkerHostId/SlotId/ExecutionId)
```

Each phase lands when a later phase *consumes* what it ships. Baton is first because `WorkerCommandHub` exists and has zero production callers. Coda cannot land before Baton because verification needs a session-capable run. Critic-sweep rides the foundation phase. Section phases in the four unused `ProjectSettings` bool flags. Encore and Tuner ride Baton. Instrument is last because every prior phase is the consumer that proves the SPI.

### 2. Live-session mode is a *harness capability*, not a Translator flag

`Capabilities { LiveSession: bool, ... }` is a field of `IHarness`. The Translator reads the value at worker start and chooses the appropriate spawning strategy (`pi --mode json` with an in-process session transport for `LiveSession = true`; the existing `--no-session` one-shot for `LiveSession = false`). `InjectContext` is *authoritative* only when the active execution's harness declares `LiveSession = true`; the platform falls back to "stage a new research WorkItem" otherwise (per coworker tasks 11.1 — the "Orchestrator command handling in the worker" task).

### 3. Baton steering is the first production caller of `WorkerCommandHub`

```text
REST POST /api/v1/runs/{runId}/steer
  → REST handler resolves runId → WorkItem → leased_by → ExecutionId
  → WorkerCommandHub.TrySendTurnInput(ExecutionId, new TurnInput { Text, ... })
  → bidi command stream delivers to the Translator
  → Translator forwards as a session turn on the live process
```

The `runId → leased_by → ExecutionId` resolver lives in `Comuki.Host/Runs/` (next to the existing `HostCancelRunAdapter.cs` — there is no separate `Comuki.Host.Operators` project; both concerns compose in the existing `Comuki.Host` host) and is shared with the existing run-cancel resolver — the same claim row carries both. The REST handler is a thin adapter over `WorkerCommandHub`; no new policy layer. The handler is decorated with `[RequiresFeature(Features.Steering)]` (one of the seven new editions feature keys) — Phase 1 is the first paid-feature call-site.

`WorkerCommandHub` and the backing virtual-key store are in-process, single-replica (D11). The first production caller is a deliberate *consequence* of that constraint: a second replica would silently miss the injection turn and the operator would see "I sent a steer, nothing happened". The constraint is recorded in the design rather than hidden, so the second-replica story is the next call out of this design.

### 4. Verification is an orthogonal axis; never a run state

`VerificationRecord` is a per-WorkItem row in a sibling table (`verifications`, indexed on `(work_item_id, gate_name)`), not a column on `work_items`. Each gate verdicts `pending` → `passed` / `failed`. A run's terminal state is *unchanged* by the verification record; the platform exposes a *visible* "verification pending" annotation through a derived view, and a *gating* policy is configured per-project through `ProjectSettings.VerifyEnabled` (existing field, 0 consumers today).

The choice to keep the run state machine closed is non-negotiable: the seven run states are guarded in `WorkItemQueueSql.cs` and the golden fixtures in `tests/unit/Comuki.Engine.Orchestration.Unit.Eval`. Inserting an eighth state would require touching the table, the guard, the fixtures, the reaper, the cancel path, the approve path, and the terminal reconciliation — a disproportionate cost for a feature the LLM should never be the authority on.

### 5. The gate-provider registry is an SPI, not a code path

```csharp
public interface IVerificationGateProvider
{
    string GateName { get; }            // "commands-green" | "peer-review" | "human-approval" | ...
    bool AppliesTo(WorkItem item, ProjectSettings settings);
    Task<GateVerdict> EvaluateAsync(WorkItem item, RunContext context, CancellationToken ct);
}
```

The set is genuinely open — operators will write their own gates. The first registered provider is the existing `Verify` module (master, `Verify:Verifier:Enabled=false`, `GenericCommandRun`). The platform ships the registry, the per-gate journal event, the evidence storage, and the work-item-side row. Provider authors register through `AddVerificationGateProvider<T>()` in the host composition root.

### 6. Evidence is journal + MinIO; no parallel store

The worker-side upload endpoint (`POST /workers/{workItemId}/artifacts`) extends its mime allow-list to include `text/x-diff`. The artifact bundle's `changeset.diff` member is the textual evidence a gate can read without an OOB fetch. The MinIO bundle layout is the existing `{project}/{run}/` prefix; gate verdicts do not reach into MinIO unless the gate author chooses to.

### 7. Section is four fixed layers in fixed precedence

```text
orchestra → section → worker → card
   ↑         ↑         ↑        ↑
   global   group    per-run  per-card
```

`orchestra` is the first *write* path to global — `GET /api/v1/settings` becomes `PATCH /api/v1/settings` with optimistic concurrency on a single row. `section` is a new entity, declared per-project, that groups workers (a container's labels analogue). `worker` is the per-run override layer; existing `ProjectSettings` move into it. `card` is the per-card override layer; **its implementation rides the mission-cowork entity landing** and is filed as deferred work in this change.

The precedence resolver is a single `IProjectSettingsSnapshotCache`-style adapter. The pattern is already in use by `ProjectScaleSettingsAdapter` and is the documented template. Editions limits and scope layers are *independent axes* — `Limits.Projects = 1` in Community does not interact with the `scope-layers` feature key; the architecture test `EditionsRegistryContainsEveryGateKeyShould` does not need to cross-reference it, but the docs do.

### 8. Encore is a read-model object, not a schema fork

```text
Automation (read-model)
  ├─ trigger: { source: ScheduledJob | SourceConnection | RunTerminal | ... }
  ├─ action: AutomationAction (typed record, replaces the three hard-coded verbs)
  ├─ history: list of AutomationRunAuditRow
  └─ template: AutomationTemplate
```

The trigger-provider registry is **deferred** until a non-cron/non-webhook trigger arrives. The action library ships with the three existing actions as `LaunchRun` / `Park` / `SyncBack`; new actions register through a typed `AddAutomationAction<T>()`. The history is the existing `IntakeDelivery` / `SyncJob` audit rows read through a dedicated adapter — no new table.

`Outbox` adoption is *per leg*: if an automation side-effect needs exactly-once-observable delivery (e.g. `SyncBack` after a Run terminal), the existing `OutboxMessage` table is used; otherwise the read-model + idempotency-key is enough. `NoopOutboxPublisher` is the current in-process publisher; the design does not add a transport.

### 9. Tuner mutates the in-process store; budget goes through `NullBudgetGate`

`PATCH /api/v1/proxy/keys/{id}` becomes 200 with the new model and limit. The new value lands in `IVirtualKeyStore` (today `ConfigurationVirtualKeyStore` + in-memory overlay; the overlay becomes the *primary* store for tunings). The live agent sees the change through the existing command channel (Baton's machinery). The throttled limit is *enforced* by the existing `NullBudgetGate` (costs); Tuner does not introduce a parallel budget path.

The model-placement advertisement of cowork 11.2 is **coordinated**, not duplicated. If cowork 11.2 ships a model-placement-params-on-host shape, Tuner consumes it; otherwise Tuner keeps the per-key model field and treats the placement as a `NullBudgetGate` problem.

### 10. Instrument: `IHarness`, with `Comuki.TestFakePi` as the second implementation

```text
IHarness
  ├── Name (string, dot.case, "pi" | "test-fake-pi" | ...)
  ├── Capabilities (HarnessCapabilities, incl. LiveSession)
  ├── ResolveEnv (Project + Profile → Dictionary<string, string>)
  ├── ParseEvent (stream-json line → canonical event record)
  └── StartAsync (ProcessStartInfo + env + working directory → Task<HarnessProcess>)
```

`Comuki.TestFakePi` (today a swap-in replacement for `PiExecutable`) becomes the second `IHarness` implementation. Its declared `Capabilities = { LiveSession = false }` (the test fake does not own a session) is the canonical negative case for Baton.

The phase ships *with* cowork 11.1 (slot/execution identity). A standalone SPI declared before the contract lands is a contract that will move; declaring it in the same change as the consumer that uses it is the only way to keep the SPI honest.

### 11. Single-replica is the supported topology for the duration of this change

`WorkerCommandHub` (Baton), the mutable `IVirtualKeyStore` (Tuner), the in-process event hub (Critic-foundation) are all single-replica. A second Host replica would silently miss the injection turn (Baton), the limit change (Tuner), or the same event (Critic). The single-replica constraint is recorded here rather than hidden:

- Deployment: a single Host replica, no `replicas: 2` in the compose file.
- Health probe: a `comuki.orchestra.single_replica` check reports the active replica count and goes Unhealthy when > 1.
- Runbook: the next architectural change is the multi-replica coordinator (Redis backplane, claim fencing on a shared lease); the design contract for that work is *recorded* in the cowork `add-mission-cowork/tasks.md` §10.1 (where `IRealtimeBackplane` is the type) and `add-mission-cowork/design.md` §12, *not* in the main `openspec/specs/realtime/spec.md` (which only enumerates "realtime backplane" as prose, no type — that file documents the v1.x SignalR hub surface). The cowork delta is the authoritative source of the multi-replica seam.

The constraint is not a regression; it is a *declared* limitation that the existing deployment already lives with (the in-memory virtual-key store is process-local today; Baton is the first user of the same single-replica assumption). The change does not relax it; it does not pretend it doesn't exist.

### 12. Editions: seven new feature keys, first non-Community call-sites

| Feature key | Phase | Tier | First gate call-site |
|---|---|---|---|
| `steering` | Baton | paid | `[RequiresFeature(Features.Steering)]` on `POST /api/v1/runs/{runId}/steer` |
| `critic` | Critic-foundation + Critic-sweep | paid | `WorkerFeatureGate` on `CriticSweepWorker` |
| `verification` | Coda | paid | `[RequiresFeature(Features.Verification)]` on the gate-provider registration host handler |
| `automation` | Encore | paid | `[EditionFeature(Features.Automation)]` on the automation module installer |
| `model-control` | Tuner | paid | `[RequiresFeature(Features.ModelControl)]` on `PATCH /api/v1/proxy/keys/{id}` |
| `scope-layers` | Section | community | (community — no gate needed) |
| `harness-spi` | Instrument | community | (community — no gate needed) |

The `EveryPaidRegistryEntryIsGatedShould` architecture test is the build-gate; the first non-Community call-sites above are what makes the test green. The capability-matrix is regenerated by `tools/Comuki.Codegen.Editions` on every Debug build; no hand-edit.

### 13. Migration: the same work substrate, additive only

Every phase is *additive*. No existing wire format changes. No existing table is renamed. No existing state machine is widened. The four unused `ProjectSettings` bool flags are *wired*, not renamed. The proxy and the WorkerCommandHub become *used*; the existing data they hold is not migrated.

WorkerCommandHub's existing commands stay. `InjectContext` gains one new caller under the v2 surface (Baton's `TurnInput` lands as an authoritative session turn; the reaper's existing injection is the v1.x path and remains the lease-loss path). The `Stop`, `LeaseExpired`, and `Exec` commands stay on the existing callers — `Stop` and `LeaseExpired` on the reaper, `Exec` on the operator-debug path; the existing `harden-pi-worker-sandbox` §5.3 contract is unchanged. The kill switch (`Stop`) remains wired to the reaper only; the multi-caller question for `Stop` is recorded as a follow-up for the multi-replica coordinator (D11).

## Risks / Trade-offs

- **[Risk] Baton is invisible until something fails live** → Phase 1 ships the steering endpoint with a *type-checked* test (an injected turn lands on a fixture harness's stream) and a *load-shedding* doc: if a steer is sent and no session transport is open, the response is 409 with a typed code. There is no silent swallow.
- **[Risk] Coda's verification record adds a journal write per gate** → A `gate_evaluated` event is one row, ~150 bytes; even with 10 gates per item the volume is negligible against the existing worker-report stream. The cost is accepted; the design's "orthogonal axis" framing protects the run state machine.
- **[Risk] Section's mutable orchestra is the first *write* path to global** → Optimistic concurrency on a single row, no shortcut; the existing `ProjectSettings` optimistic-concurrency contract (409 `Settings version conflict`) extends to `OrchestraSettings`. A `null` orchestra value is "use defaults"; a non-null value is "override"; precedence is fixed.
- **[Risk] Tuner mutates the in-process store while a key is in flight** → The mutable store is *monotonic-write* with a fencing check: a `tunings[].generation` integer; the worker's session transport carries the generation; a stale generation is rejected at the next command. The same fence the run state machine already uses; no new mechanism.
- **[Risk] Encore's read-model is a read-only projection of three existing sources** → Each source keeps its own audit row. The `Automation.history` view joins them through the read adapter. If a source changes its audit shape, the adapter is updated; the change does not touch the source.
- **[Risk] Instrument ships *with* cowork 11.1** → If cowork 11.1 slips, Instrument is delayed; the SPI does not get declared twice. The dependency is recorded in §Coordination notes.
- **[Risk] Single-replica is a real limitation** → Documented, probed, runbooked. D11.
- **[Trade-off] Baton forces the harness to declare `Capabilities.LiveSession` honestly** → A test harness that does not own a session is not allowed to receive `InjectContext` as authoritative. The platform falls back to "stage a new research WorkItem" (cowork 11.1 fallback).
- **[Trade-off] Section's card layer is deferred** → The orchestra / section / worker layers are functional in this change; the card layer is recorded as a follow-up riding the mission-cowork entity landing. The four-layer precedence is implemented, the four-layer population is three-and-a-half.
- **[Trade-off] Encore's trigger-provider registry is deferred** → The cron and webhook triggers already work through the existing surfaces; the registry is created when a non-cron/non-webhook trigger arrives. The interface shape is recorded in the design so the first non-cron trigger does not invent one.

## Migration Plan

```text
Phase 1 (Baton)          — session + steering endpoint
                              no existing wire change
                              WorkerCommandHub gains one caller
                              Translator gains session-mode option behind Harness.Capabilities.LiveSession

Phase 2 (Critic-foundation) — MEL→OTLP exporter + Victoria client + MCP read tools
                              existing log path gains an export leg
                              the four reading tools land in the existing MCP catalog

Phase 3 (Coda)            — verifications table + GateProvider registry
                              existing run state machine unchanged
                              Verify:Verifier:Enabled=true on first gate provider
                              ProjectSettings.VerifyEnabled reads (existing field, 0 consumers)

Phase 4 (Critic-sweep)    — StandingQuery job type
                               rides Phase 2's MCP read tools
                               uses `Comuki.Modules.Scheduler` as the substrate (the existing `IScheduledJobStore`)

Phase 5 (Section)         — Section entity + orchestra write path
                              orchestra is the first write path to global
                              ProjectSettingsAdapter splits into scope-layer resolver

Phase 6 (Encore)          — Automation read-model + action library
                              reads ScheduledJob + SourceConnection + RunStatusBridge + SyncJob
                              no schema fork; existing tables are sources

Phase 7 (Tuner)            — mutable IVirtualKeyStore + PATCH /api/v1/proxy/keys/{id}
                              existing ConfigurationVirtualKeyStore + in-memory overlay
                              overlay becomes the primary store for tunings

Phase 8 (Instrument)      — IHarness abstraction
                              rides cowork 11.1 (slot/execution identity)
                              Comuki.TestFakePi becomes the second implementation
```

Every phase is independently shippable behind a feature flag; rollback per phase is the existing "roll back the diff, leave the wire" — no data migration. The single-replica constraint (D11) is a known limitation across all eight phases.

## Open Questions

- **Phase 8 (Instrument) timing against cowork 11.1** — Both are vertical work; the right answer is "ship them together" or "ship Instrument after cowork 11.1 lands". The coordination note in §Coordination notes records the dependency direction; the *plan* is recorded here, the *date* is recorded in `STATE.md`.
- **Section's card layer** — requires the mission-cowork card entity to land first. Recorded as deferred work; not a blocker for the orchestra / section / worker layers.
- **Encore's trigger-provider registry** — interface shape is recorded; the registry is created on the first non-cron/non-webhook trigger. The dashboard `Automation` page and the `Automation` object ship without the registry.
- **Phase 2 retention defaults** — VictoriaLogs is configured with `--retentionPeriod=1` (per the platform baseline shipped in `deploy/docker-compose.yml`). The change does not lower that baseline; an explicit retention setting is a deployment-level concern, not a code-level default, and the configuration surface exposes `Observability:Victoria:RetentionPeriod` only as a typed option (no code-level default).

## Coordination Notes

These are the cross-change dependencies that the next session should be aware of. **They are not blockers**; they are recorded so the audit does not rediscover them.

- **`add-mission-cowork` (#70) §11** — `WorkerHostId` / `SlotId` / `ExecutionId` are the slot identity the WorkerCommandHub will own in slot terms. Baton lands against the *current* `WorkerId` and records "this is the v1.x steering surface; v2 steering is the slot one". Instrument is the explicit consumer of cowork 11.1 and ships after it. Coordination: cowork 11.1 is the upstream; Instrument is the downstream; if cowork 11.1 slips, Instrument is the first thing this change drops.
- **`add-mission-cowork` (#70) `missions/spec.md` §"Mission completion evidence" (line 65)** — the cowork Mission-level completion policy declares deterministic auto-verification, explicit human reviewers, or Brain-assisted review plus deterministic evidence per Task/link; failed deterministic verification blocks the Task and produces a repair/waiver proposal; Brain cannot override the verifier. **Coda's WorkItem-level gate-provider registry (this change's `verification` capability) operates below the cowork Task-level policy** — the registry produces `gate_evaluated` rows per WorkItem; the cowork Task policy consumes those rows through its completion-policy evaluator and overlays Mission status. The two layers do not compete: the WorkItem gate registry is the *evidence producer*; the cowork Task policy is the *evidence consumer*. Mission-level policy (Brain vs human vs auto-verification) is layered above both, in the Mission's completion-policy evaluator.
- **`add-mission-cowork` §4 (Minimal Mission Coordination)** — `IProjectSettingsSnapshotCache` is the pattern the Section precedence resolver clones. The pattern is documented; the implementation is local.
- **`add-mission-cowork` §15 (Templates)** — `Automation` and `Verification` templates are not in this change. They ride cowork §15.
- **`add-multi-repo-projects` (#163) §R7/R15** — cross-project Mission / Section inheritance; the `Section` aggregate respects the same `parent ∩ section` scope that Missions do. The change does not fork the scope shape.
- **`add-multi-repo-projects` (#163) §"Relationship to issue #50"** — dependency-ordered merge batches (issue #50) are subsumed structurally by #163 (per-repo queues + cross-repo DAG in flow 3). This change's Coda phase (task 3.6) restores the **run reference** on `MergeQueueEntry` / `MergeBatch` (`RunId?`, nullable for release trains) but does **not** add a dependency graph — the dependency-ordering work is already covered by #163. Coordination: the run reference is the only link the Coda phase adds; #163 owns the rest.
- **`harden-pi-worker-sandbox` (#121) §4–5** — `SourceGitUrl` / `SourceGitRef` / `git-credential` / `WorkspacePrepared` / `EgressApplied` / `AgentRunning` are already on master; this change depends on them and they are not part of the verification (the harden change's own checkboxes are the proof). Tasks 4.1, 4.2, 4.3, and the partial 5.1 of `harden-pi-worker-sandbox/tasks.md` are *closed* with code references in this change's `tasks.md` §9 (see that file).
- **OpenSpec capabilities directory** — seven new directories land under `openspec/changes/add-orchestra/specs/` (one per new capability: `automation` / `harness-spi` / `model-control` / `observability` / `scope-layers` / `session` / `verification`); four `## MODIFIED Requirements` deltas land under the existing capability directories (`worker-runtime` / `artifacts` / `projects` / `host`). The other capabilities this change touches at the implementation level (`editions` / `identity` / `runs` / `intake` / `compute` / `scheduler`) ship their changes as `## ADDED Requirements` in this change's spec deltas, or as implementation-only changes that do not require a spec delta (e.g. `intake` is read through a dedicated adapter; the wire format is unchanged). The main `openspec/specs/` tree is not edited in this change — the deltas archive into main when each phase lands.
- **Editions capability-matrix regeneration** — the seven new feature keys flow through `tools/Comuki.Codegen.Editions` on every Debug build; the commit that adds the rows also regenerates `openspec/specs/editions/capability-matrix.md` and the dashboard's `dashboard/src/shared/editions/_generated/registry.ts`. The matrix is the source of truth, not the hand-edit.
