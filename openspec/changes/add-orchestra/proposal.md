## Why

Comuki owns the substrate of an agent fleet — a run, a worker, a journal, a thin chat — but the *product* surface is missing: no live steering, no verification axis, no log readback, no automation read-model, no mutable proxy keys, no second harness. The eight phases of this umbrella close the gap; they share one constraint set (same runtime, edition model, journal substrate) so they ship as one OpenSpec change (precedent: `add-mission-cowork`). Phase names are **branding** (Baton / Coda / Section / Encore / Tuner / Instrument / Critic), not project renames.

`add-mission-cowork` (#70) stays a separate change — Mission/Task is the work substrate this change consumes; the dependency direction is declared in `design.md` §Coordination notes.

## What Changes

- **Baton** — Translator runs `pi` in *session* mode; `POST /api/v1/runs/{runId}/steer` resolves `runId → leased_by → ExecutionId` and delivers a `TurnInput` through the existing `WorkerCommandHub`. First production caller of the channel.
- **Critic** *(foundation + sweep)* — MEL exports to OTLP; VictoriaLogs/Metrics are readable through a typed client and four MCP tools; a scheduled sweep worker reads the same logs/metrics through MCP and decides whether to file a native ticket or write a memory note.
- **Coda** — verification is an **orthogonal axis**, not a new run state. `VerificationRecord` is a per-WorkItem journal of gate verdicts; a typed gate-provider registry (SPI) and the existing `Verify` module (master, `Verify:Verifier:Enabled=false`) become the first provider.
- **Section** — four scope layers in fixed precedence: orchestra (mutable global, first *write* path) → section (sibling aggregate) → worker (per-run overrides) → card (per-card; **deferred** to the cowork entity landing).
- **Encore** — `Automation` becomes a first-class *read-model* on top of `ScheduledJob`, `SourceConnection` / webhook, and `RunStatusBridge` + `SyncJob`. Trigger-provider registry deferred.
- **Tuner** — virtual keys are mutable; `PATCH /api/v1/proxy/keys/{id}` becomes 200; changes propagate through Baton.
- **Instrument** — `IPiRunner` becomes `IHarness`; harness declares `Capabilities.LiveSession`. Ships *with* cowork 11.1.

Order is **dogfooding-first**: Baton (channel has zero production callers) → Coda (verification needs a session-capable run) → Critic on the foundation → Section (the four `ProjectSettings` flags with zero consumers deserve to *mean* something) → Encore / Tuner ride Baton → Instrument last (every prior phase is the SPI consumer).

## Capabilities

### New Capabilities

- `session`: live-session steering and the production caller of `WorkerCommandHub`.
- `observability`: MEL→OTLP log export, typed Victoria client, four MCP read tools.
- `verification`: per-WorkItem gate record, gate-provider registry (SPI), Evidence record, the existing `Verify` module as the first provider.
- `scope-layers`: four-level precedence resolver, mutable orchestra globals, section adapter.
- `automation`: `Automation` read-model object, action library, run history, optional outbox leg, dashboard domain.
- `model-control`: mutable `IVirtualKeyStore` and the live-run model switch.
- `harness-spi`: second-class-to-first-class harness abstraction; declares env, event schema, capabilities (incl. `LiveSession`).

### Modified Capabilities

Four existing capabilities carry `## MODIFIED Requirements` deltas (the canonical "modified" surface in OpenSpec):

- `worker-runtime`: live-session mode, steering as authoritative `InjectContext` for session-capable harnesses, `Capabilities.LiveSession`.
- `artifacts`: bundle includes a `changeset.diff` member under `text/x-diff`.
- `projects`: section aggregate; `ProjectSettings.VerifyEnabled` is wired.
- `host`: mutable `IVirtualKeyStore`; live `PATCH /api/v1/proxy/keys/{keyId}`; throttled limits flow through `NullBudgetGate`.

Three existing capabilities carry `## ADDED Requirements` deltas (the canonical "new requirement" surface in OpenSpec; the existing requirements stay unchanged):

- `runs`: `VerificationRecord` annotation is a derived read; the seven-state run transition table is unchanged.
- `identity`: `WorkerFeatureGate` semantics stay; the **first** real consumer of `[RequiresFeature]` for a non-Community feature lands.
- `editions`: seven new `Feature.Define(...)` rows — `steering` / `critic` / `verification` / `automation` / `model-control` on paid tier; `scope-layers` / `harness-spi` on Community.

Three existing capabilities are touched at the implementation level only and carry no spec delta in this change (the change is *additive* — the existing surface is unchanged; the new behaviour is exposed through the `verification` / `model-control` / `automation` deltas above, not through a `intake` / `scheduler` / `compute` spec delta):

- `intake`: no wire-format change; the `Automation` object reads `IntakeDelivery` / `RunStatusBridge` / `SyncJob` through a dedicated adapter.
- `scheduler`: `StandingQuery` typed job reading through the existing `IScheduledJobStore` (the v1.x scheduler records the fire trail on the `jobs` row itself; a stand-alone `firings` history table is the open follow-up from the unarchived `add-scheduled-jobs` change).
- `compute`: profile shape read from `IHarness.Capabilities` (the capability home is `harness-spi`; the `compute` module is the consumer).

## Impact

`Comuki.Host.Translator/`, `WorkerCommandHub.cs` (first real producer/consumer of session-mode execution); `Comuki.Engine.Orchestration/`, `WorkQueue/` (verification-pending phase in terminal reconciliation); `Comuki.Modules.Projects/` (Section aggregate; scope-layers adapter); `Comuki.Modules.Scheduler/` (StandingQuery job type); `Comuki.Modules.Intake/` (Automation read adapter); `Comuki.Modules.Proxy/` (mutable store); `Comuki.Engine.Compute/` (IHarness resolution); `Comuki.Shared.Editions/Features.cs` (seven new rows — five paid + two community); `dashboard/src/` (Automation domain; settings PATCH; harness picker); `agents/comuki-worker-sdk/` (IHarness consumer). Issue #70 (`add-mission-cowork`) remains the umbrella epic for the work substrate and is not subsumed.

## Non-goals

- Replacing the run state machine. `Run = Succeeded` is one of seven states; `verification-pending` is a *visible* annotation, not a new state.
- Suspend/resume or `ax ssh`-style guest APIs. The session contract is what `pi --mode json --no-session` already supports *plus* an injection turn, not a parallel control plane.
- Multi-replica coordinator for `WorkerCommandHub` and the mutable virtual-key store. Single-replica is the supported topology for the duration of this change.
- Replacing `WorkerId` with `WorkerHostId` / `SlotId` / `ExecutionId` *here*. The Instrument phase declares the SPI in slot terms and ships *with* cowork 11.1.
- Replacing v1 virtual keys with raw upstream secrets.
- Authoring any code. This change is the *plan*, not the implementation.