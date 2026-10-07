# Session Specification

## Purpose

Defines the live-session steering surface for an in-flight run: how the platform delivers a `TurnInput` to a running worker through the existing bidi command channel, how the harness's `Capabilities.LiveSession` declaration turns the steering into an authoritative session turn, and how the v1.x `WorkerId` ownership model and the cowork 11.1 `ExecutionId` model coexist on the same wire.

This capability is the first production caller of `WorkerCommandHub`. Today the channel ships with `Stop` / `InjectContext` / `LeaseExpired` / `Exec` commands; the kill switch (`Stop`) is wired only to the reaper, the other commands sit on the channel without live callers. Baton is what makes the channel a product surface.

## ADDED Requirements

### Requirement: Steering endpoint resolves runId to a live execution

`POST /api/v1/runs/{runId}/steer` (route constant `ApiRoutes.RunSteer`) SHALL resolve, in a single read path, `runId → WorkItem → LeasedBy → ExecutionId` and SHALL send the typed turn through `WorkerCommandHub.TrySendTurnInput(ExecutionId, TurnInput)`. The endpoint requires `run:read` plus `[RequiresFeature(Features.Steering)]`. The same resolver is reused by the existing run-cancel endpoint; the resolver is a single shared helper under `Comuki.Host/Runs/` (next to the existing `HostCancelRunAdapter.cs` — there is no separate `Comuki.Host.Operators` project; the host composes both concerns in `Comuki.Host`).

#### Scenario: Steer lands on a live session

- **WHEN** an authorised caller POSTs a steer body to a run whose work-item lease is currently held by a worker
- **THEN** the worker receives a `TurnInput` command on the bidi stream within one heartbeat interval and the API returns `202 Accepted` with `{ delivered: true }`

> **`delivered:true` semantics.** `delivered:true` is the orchestrator's confirmation that the `TurnInput` command was written into the bidi command channel (`WorkerCommandHub.TrySendTurnInput` returned `true`); it is **not** a guarantee the harness process has consumed the turn. Delivery into the channel is the orchestrator's responsibility; whether and when the harness's stdin writer picks the command up is the harness's own lifecycle, observable downstream through the harness's journal (worker-side `TurnInput`-consumed events on the bidi events stream). The 202 carries `delivered` for both branches (bidi and follow-up) so the caller cannot tell the harness was a `LiveSession = false` declaration — the surface is uniform; the difference is the body shape (`followUpWorkItemId` set on the no-live branch).

#### Scenario: Steer misses without a live stream

- **WHEN** an authorised caller POSTs a steer for a run whose work-item lease is not currently held (the worker dropped the stream between lease-mint and steer)
- **THEN** `WorkerCommandHub.SendAsync` returns `false`, the API returns `202 Accepted` with `{ delivered: false }`, and the caller may retry

#### Scenario: Steer for an unknown run

- **WHEN** an authorised caller POSTs a steer for a `runId` that does not exist
- **THEN** the response is `404 Not Found` `application/problem+json` with no leak of state

#### Scenario: Steer without the feature key

- **WHEN** a Community-tier request reaches the endpoint
- **THEN** the response is `403` with `code = edition.feature_unavailable` and `featureKey = steering`

### Requirement: TurnInput is the authoritative session turn

`TurnInput` is a structured record `{ Text, Role, Metadata }`. The Translator forwards `TurnInput` as a *session turn* on the live agent process — equivalent to the user's next chat input on a fresh interactive session — and the agent's authoritative final wording becomes the next run summary.

`TurnInput` is **authoritative exactly when** the active execution's harness declares `Capabilities.LiveSession = true` (the `harness-spi` capability, Phase 1). On a harness that declares `LiveSession = false`, the platform falls back to "stage a new research WorkItem" per the cowork 11.1 fallback path — the no-session-follow-up isn't a refusal, it's the canonical outcome. The fallback is what a `LiveSession = false` harness looks like in practice today (the only such harness the production host registers is `TestFakeHarness`); a profile with no explicit `harness:` frontmatter falls back to `pi` (the canonical prod harness, `LiveSession = true`), so the follow-up path is what test-fake-pi profiles exercise and what Phase 8 / Instrument turns into a 409 when an explicit non-default harness declares `LiveSession = false` outright.

> **Coordination note (2026-10-04, add-orchestra + add-mission-cowork wiring).** This change implements the **live-session declaration** that `add-mission-cowork/specs/worker-runtime/spec.md` (Requirement "Orchestrator command handling in the worker", the cowork delta) anchors: `InjectContext` (and by extension `TurnInput`) **IS authoritative exactly when** the active execution's harness declares `Capabilities.LiveSession = true`. The cowork delta's clause "is *not* an authoritative turn on a session-capable harness" was the inverse of the intended reading — this change clarifies the semantics: the harness declaration *promotes* InjectContext/TurnInput to authoritative; without the declaration, the fallback path is the only correct behaviour. `add-mission-cowork` files are not edited from this change; the next cowork archive pass rewords the cowork-side clause in line with this clarification.

#### Scenario: a horse-less injector lands as follow-up WorkItem

- **WHEN** a steer is sent for a run whose harness declares `Capabilities.LiveSession = false`
- **THEN** the platform stages a follow-up research `WorkItem` (cowork 11.1 fallback) and the API returns `202 Accepted` with `{ delivered: true, followUpWorkItemId }`. The follow-up is the canonical outcome for any `LiveSession = false` harness today; the `409 Conflict` with `code = session.livesession_unavailable` path is reserved for Phase 8 / Instrument — an explicit, intentional `LiveSession = false` declaration on a non-default harness that rejects `TurnInput` outright (a separate wire shape from the follow-up path).

#### Scenario: Authoritative turn replaces accumulated text

- **WHEN** the agent session streams text deltas and later emits a message-end authoritative assistant text after a steer
- **THEN** the worker's authoritative-text rule replaces the accumulated deltas with the post-steer final wording (the existing `Result text is authoritative` scenario in worker-runtime)

### Requirement: Capabilities.LiveSession is declared by the harness

`IHarness.Capabilities.LiveSession` is a `bool` field of the harness capabilities record. The platform reads the value at worker start and chooses the spawn strategy: `LiveSession = true` opens the bidi command channel and accepts `TurnInput`; `LiveSession = false` does not. The field is the single source of truth for "can a steer land here authoritatively?"; the Translator does not consult any other field.

#### Scenario: pi declares LiveSession = true

- **WHEN** a Translator starts a `PiHarness` execution
- **THEN** `Capabilities.LiveSession` is `true` and the bidi command channel is open for `TurnInput`

#### Scenario: Test-fake harness declares LiveSession = false

- **WHEN** a Translator starts a `TestFakeHarness` execution
- **THEN** `Capabilities.LiveSession` is `false` and the steering endpoint refuses `TurnInput` for that run with `code = session.livesession_unavailable`

### Requirement: Steering is a single-replica feature

`WorkerCommandHub` is in-process and single-replica. A second Host replica would silently fail to deliver `TurnInput` to a worker held by the other replica. The platform SHALL expose a `comuki.orchestra.single_replica` health check that reports the active replica count and goes Unhealthy when > 1; deployments with `replicas: 2` SHALL not enable the steering endpoint in production until the multi-replica coordinator ships (the design contract is recorded; the runtime is a separate change).

#### Scenario: Two-replica deployment fails the health check

- **WHEN** the Host is running with `replicas: 2` in production
- **THEN** `comuki.orchestra.single_replica` is Unhealthy and `/health/ready` returns 503

#### Scenario: Single-replica deployment passes

- **WHEN** the Host is running with `replicas: 1` in production
- **THEN** `comuki.orchestra.single_replica` is Healthy and `/health/ready` returns 200

### Requirement: RunId-to-ExecutionId resolver is shared

The `runId → WorkItem → LeasedBy → ExecutionId` resolver SHALL be a single shared helper under `Comuki.Host/Runs/RunResolver.cs` (the exact filename is implementation detail; the seam is `IExecutionIdResolver`). The run-cancel endpoint and the run-steer endpoint SHALL both call through the seam. A new resolver variant per endpoint is forbidden. The seam lives in `Comuki.Host` next to the existing `HostCancelRunAdapter.cs`; there is no separate `Comuki.Host.Operators` or `Comuki.Host.Api` project.

#### Scenario: Both endpoints use the same resolver

- **WHEN** the cancel endpoint and the steer endpoint are both mounted
- **THEN** the architecture test `RunResolverIsSharedShould` asserts a single `IExecutionIdResolver` implementation is registered in the DI graph

## ADAPTER Notes

`WorkerCommandHub` is the contract surface; this capability is the first caller. The v1.x ownership model (one `WorkerId` per lease) is the contract Baton delivers against. The cowork 11.1 slot/execution identity model is the v2 surface; when 11.1 lands, the same `TurnInput` command travels through the same `WorkerCommandHub` and the `WorkerId` parameter becomes a `SlotHandle`. The change does not redesign `WorkerCommandHub`; it consumes the existing channel. The v1.x `WorkerId` model is preserved as the contract; the future-cite for the cowork 11.1 slot-binding language is recorded in the `worker-runtime/spec.md` MODIFIED blocks of this change (see the ARCHIVE-ORDER CONSTRAINT in that file).
