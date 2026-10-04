## MODIFIED Requirements

### Requirement: Optional OpenAI / Anthropic proxy

The host SHALL expose an optional YARP-backed proxy that forwards OpenAI- and Anthropic-compatible HTTP requests to the upstream provider the caller's virtual key is bound to. The proxy runs in-process inside the orchestrator host — no separate container.

Routes:

| Method | Path | Upstream cluster |
|--------|------|------------------|
| `POST` | `/v1/chat/completions` | `openai` (configurable) |
| `POST` | `/v1/messages` | `anthropic` (configurable) |
| `GET`  | `/v1/models` | static catalogue from `Proxy:KnownModels` |

Authentication: every proxy route SHALL require the `VirtualKey` authentication scheme (`Authorization: Bearer vkey_xxx`). The scheme authenticates the bearer against the `Proxy:VirtualKeys[]` catalogue (token / expiry / allowed-models), selects the upstream cluster the configured virtual key points at, rewrites the outbound `Authorization` header to the upstream API key the virtual key's `ApiKeyEnvRef` names, and strips the inbound `Host` header so the upstream sees its own host.

When `Proxy:Enabled` is `false`, the proxy module SHALL register no routes and the VirtualKey scheme SHALL refuse every request.

> **Coordination note (2026-10-04).** This change's **Tuner** phase adds `PATCH /api/v1/proxy/keys/{keyId}` and promotes the in-memory overlay to the primary store for tunings. The full model-control contract (Tuning shape, `IVirtualKeyStore.ApplyTuningAsync`, `Generation` fencing, `NullBudgetGate` cooperation, live `WorkerCommandHub` propagation) lives in the **`model-control` capability** — the `host` capability owns the proxy routes and references the model-control delta for the mutation surface. The virtual key catalogue contract (token / expiry / allowed-models / BudgetUsd) is unchanged in shape — `BudgetUsd` becomes a *mutable* field on the key. The proxy routes (`POST /v1/chat/completions`, `POST /v1/messages`) are unchanged.

#### Scenario: Anonymous request is rejected

- **WHEN** a caller POSTs `/v1/chat/completions` with no `Authorization` header
- **THEN** the host answers `401 Unauthorized` and the upstream never sees the request

#### Scenario: Unknown virtual key is rejected

- **WHEN** a caller POSTs `/v1/chat/completions` with `Authorization: Bearer vkey_unknown`
- **THEN** the host answers `401 Unauthorized`

#### Scenario: Valid virtual key forwards to the upstream

- **WHEN** a caller POSTs `/v1/chat/completions` with `Authorization: Bearer vkey_alpha` and `vkey_alpha` is configured with `Provider = "openai"`, `BaseUrl = "https://api.openai.com"`, `ApiKeyEnvRef = "OPENAI_API_KEY"`
- **THEN** the upstream receives the request with `Authorization: Bearer ${OPENAI_API_KEY}` and the upstream's response is returned to the caller unchanged

#### Scenario: Expired virtual key is rejected

- **WHEN** a caller POSTs with `Authorization: Bearer vkey_alpha` and `vkey_alpha.ExpiresAt <= now`
- **THEN** the host answers `401 Unauthorized`

#### Scenario: Model outside allow-list is rejected

- **WHEN** a caller POSTs `/v1/chat/completions` with `Authorization: Bearer vkey_alpha`, `vkey_alpha.AllowedModels = ["gpt-4o-mini"]`, and the body requests `model = "gpt-4"`
- **THEN** the host answers `401 Unauthorized`

## ADDED Requirements

### Requirement: PATCH /api/v1/proxy/keys/{keyId} is the mutable key surface

`PATCH /api/v1/proxy/keys/{keyId}` (route constant `ApiRoutes.ProxyKeyPatch`) SHALL accept a `Tuning` body `{ Model?, LimitUsd? }` and SHALL return `200` with the updated virtual-key view and the new `Generation`. The endpoint requires the new `model-control:write` permission AND `[RequiresFeature(Features.ModelControl)]` (the existing `proxy:write` is the surface for revoke / mint / revoke-mint operations on the existing proxy key store; model tuning is a separate, model-control-only surface — see the `model-control` capability for the canonical access model). The handler invokes `IVirtualKeyStore.ApplyTuningAsync(...)`; the in-memory overlay records the new value and the `Generation` increments.

#### Scenario: Authorised patch succeeds

- **WHEN** an authorised caller PATCHes `/api/v1/proxy/keys/key_alpha` with `{ model: "gpt-4o-mini" }`
- **THEN** the response is `200` with the updated view, the next proxy request under `key_alpha` uses the new model, and the `Generation` is incremented by one

#### Scenario: Patch without the feature key

- **WHEN** a Community-tier caller PATCHes the same endpoint
- **THEN** the response is `403` with `code = edition.feature_unavailable` and `featureKey = model-control`

#### Scenario: Lowering a limit under the running cap

- **WHEN** an authorised caller PATCHes `/api/v1/proxy/keys/key_alpha` with `{ limitUsd: 50 }` and the proxy-source spend this month is `$45`
- **THEN** the new limit is in force; subsequent calls under `$5` total proceed normally

#### Scenario: Lowering a limit past the running cap

- **WHEN** an authorised caller lowers the limit to `$40` with the same running spend
- **THEN** the existing `NullBudgetGate` (costs) rejects the next call with `429` and `Retry-After`; the rejection path is unchanged

### Requirement: Limit cooperation with NullBudgetGate

The throttled `LimitUsd` field on the virtual key SHALL be enforced through the existing `NullBudgetGate` (costs). The cooperation is through the same `usage_events.cost_usd_micros` query the budget gate already uses. The only change is that `BudgetUsd` becomes a *mutable* field on the key (today it is set at creation).

#### Scenario: Limit mutable is honoured by the budget gate

- **WHEN** an authorised caller mutates `LimitUsd` upward
- **THEN** the next proxy request that would have been blocked under the old limit now proceeds under the new limit

#### Scenario: Mutable limit does not change verdict semantics

- **WHEN** the next request hits the cap
- **THEN** the existing `NullBudgetGate` answer is `429 Too Many Requests` with `Retry-After` set to the seconds until the next calendar month — no new code path

### Requirement: Live propagation through WorkerCommandHub

A tuning that targets a *running* key SHALL send a `ModelChanged` command through the existing `WorkerCommandHub` (the same channel the `session` capability declares). The harness reload reads the new model from the session transport.

#### Scenario: Model change reaches the worker

- **WHEN** an authorised caller mutates the model on a key whose worker is alive
- **THEN** the worker receives a `ModelChanged` command on the bidi stream within one heartbeat interval and the next model call uses the new model

#### Scenario: Worker dropped the stream mid-flight

- **WHEN** an authorised caller mutates the model on a key whose worker dropped the stream between lease-mint and tuning
- **THEN** the propagation returns `false` (best-effort); the operator may retry; the in-memory store still records the tuning

### Requirement: Model placement is coordinated with cowork 11.2

If cowork 11.2 ships a model-placement-params-on-host shape, Tuner consumes it; otherwise Tuner keeps the per-key model field and treats placement as a `NullBudgetGate` problem. This coordination note is recorded in `design.md` §9.

#### Scenario: Cowork 11.2 ships the placement shape

- **WHEN** cowork 11.2 lands the placement params
- **THEN** `IVirtualKeyStore.ApplyTuningAsync` reads the placement params from cowork and the per-key model field is deprecated

#### Scenario: Cowork 11.2 does not ship

- **WHEN** cowork 11.2 slips past Tuner's phase
- **THEN** the per-key model field remains the surface; placement is a budget-gate problem

## ADAPTER Notes

`ConfigurationVirtualKeyStore` (config-seeded keys) stays. The in-memory overlay next to it becomes the primary store for tunings. The proxy's existing `IVirtualKeyStore` interface gains one method (`ApplyTuningAsync`); existing callers continue to use the unchanged surface (`FindAsync`, `RemoveAsync`). The virtual key's `BudgetUsd` field is promoted from immutable (set at create) to mutable (tunable); the existing `NullBudgetGate` answer shape is unchanged. The proxy's three existing routes (`POST /v1/chat/completions`, `POST /v1/messages`, `GET /v1/models`) are unchanged.