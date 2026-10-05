# Model Control Specification

## Purpose

Defines the mutable virtual-key surface: `IVirtualKeyStore.ApplyTuningAsync`, the `Tuning` shape with a fencing generation, the `PATCH /api/v1/proxy/keys/{id}` promotion from `501` to `200`, the live propagation through the existing `WorkerCommandHub` (Baton), and the cooperation with the existing `NullBudgetGate` (costs) for the throttled limit.

Today the proxy stores virtual keys in process-local config; `PATCH /api/v1/proxy/keys/*` answers `501 need a mutable key store first`. The mutable store exists in-memory (the overlay next to `ConfigurationVirtualKeyStore`); this capability promotes the overlay to primary for tunings.

## ADDED Requirements

### Requirement: Tuning is a fenced record

`Tuning` is a record `{ Id, KeyId, Generation, Model?, LimitUsd?, ChangedAt, ChangedBy }`. Every write bumps `Generation`. The worker's session transport (the channel Baton declares) carries the current generation and rejects stale ones with a typed 409.

#### Scenario: ApplyTuning writes and bumps

- **WHEN** an authorised caller invokes `IVirtualKeyStore.ApplyTuningAsync(new Tuning { KeyId = "key_alpha", Model = "gpt-4o-mini" }, ct)`
- **THEN** the in-memory overlay records the new value and the key's `Generation` increments by one

#### Scenario: Stale generation rejects with a typed 409

- **WHEN** a caller invokes a write with a `Generation` below the current value
- **THEN** the response is `409 Conflict` with `code = proxy.tuning_generation_mismatch` and the in-memory store is unchanged

### Requirement: PATCH /api/v1/proxy/keys/{keyId} becomes 200

`PATCH /api/v1/proxy/keys/{keyId}` SHALL be promoted from `501` to `200` with the typed `Tuning` body. The handler requires `[RequiresFeature(Features.ModelControl)]` AND the new `model-control:write` permission (introduced by this change, see the `identity` capability). The existing `proxy:write` permission is **not** the gate for this endpoint — `proxy:write` is the surface for revoke / mint / revoke-mint operations on the existing proxy key store; model tuning is a separate, model-control-only surface. The response carries the updated key view and the new `Generation`.

#### Scenario: Authorised patch succeeds

- **WHEN** an authorised caller PATCHes `/api/v1/proxy/keys/key_alpha` with `{ model: "gpt-4o-mini" }`
- **THEN** the response is `200` with the new view and the next proxy request under `key_alpha` uses the new model

#### Scenario: Patch without the feature key

- **WHEN** a Community-tier caller PATCHes the same endpoint
- **THEN** the response is `403` with `code = edition.feature_unavailable` and `featureKey = model-control`

#### Scenario: Lowering a limit under the running cap

- **WHEN** an authorised caller PATCHes `/api/v1/proxy/keys/key_alpha` with `{ limitUsd: 50 }` and the proxy-source spend this month is `$45`
- **THEN** the new limit is in force; subsequent calls under `$5` total proceed normally

#### Scenario: Lowering a limit past the running cap

- **WHEN** the same caller lowers the limit to `$40` with the same running spend
- **THEN** the existing `NullBudgetGate` (costs) rejects the next call with `429` and `Retry-After`; the rejection path is unchanged

### Requirement: Limit cooperation with NullBudgetGate

The throttled `LimitUsd` field on the virtual key SHALL be enforced through the existing `NullBudgetGate` (costs). Tuner does not introduce a parallel budget path. The cooperation is through the same `usage_events.cost_usd_micros` query the budget gate already uses; the only change is that `BudgetUsd` becomes a *mutable* field on the key (today it is set at creation).

#### Scenario: Limit mutable is honoured by the budget gate

- **WHEN** an authorised caller mutates `LimitUsd` upward
- **THEN** the next proxy request that would have been blocked under the old limit now proceeds under the new limit

#### Scenario: Mutable limit does not change verdict semantics

- **WHEN** the next request hits the cap
- **THEN** the existing `NullBudgetGate` answer is `429 Too Many Requests` with `Retry-After` set to the seconds until the next calendar month — no new code path

### Requirement: Live propagation through WorkerCommandHub

A tuning that targets a *running* key SHALL send a `ModelChanged` command through the existing `WorkerCommandHub` (the same channel Baton declares). The harness reload reads the new model from the session transport.

#### Scenario: Model change reaches the worker

- **WHEN** an authorised caller mutates the model on a key whose worker is alive
- **THEN** the worker receives a `ModelChanged` command on the bidi stream within one heartbeat interval and the next model call uses the new model

#### Scenario: Worker dropped the stream mid-flight

- **WHEN** an authorised caller mutates the model on a key whose worker dropped the stream between lease-mint and tuning
- **THEN** the propagation returns `false` (best-effort, like `Stop`); the operator may retry; the in-memory store still records the tuning

### Requirement: Model placement is coordinated with cowork 11.2

If cowork 11.2 ships a model-placement-params-on-host shape, Tuner consumes it; otherwise Tuner keeps the per-key model field and treats placement as a `NullBudgetGate` problem. This coordination note is recorded in `design.md` §9.

#### Scenario: Cowork 11.2 ships the placement shape

- **WHEN** cowork 11.2 lands the placement params
- **THEN** Tuner's `IVirtualKeyStore.ApplyTuningAsync` reads the placement params from cowork and the per-key model field is deprecated

#### Scenario: Cowork 11.2 does not ship

- **WHEN** cowork 11.2 slips past Tuner's phase
- **THEN** the per-key model field remains the surface; placement is a budget-gate problem

## ADAPTER Notes

`ConfigurationVirtualKeyStore` (config-seeded keys) stays. The in-memory overlay next to it becomes the primary store for tunings. The proxy's existing `IVirtualKeyStore` interface gains one method (`ApplyTuningAsync`); existing callers continue to use the unchanged surface (`FindAsync`, `RemoveAsync`). The virtual key's `BudgetUsd` field is promoted from immutable (set at create) to mutable (tunable); the existing `NullBudgetGate` answer shape is unchanged.
