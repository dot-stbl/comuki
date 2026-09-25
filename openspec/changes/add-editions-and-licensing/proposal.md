## Why

Comuki is open-core. Today the whole platform is Community: every
capability ships in one binary with no gate on what a deployment may
turn on. Paid candidates already in the issue queue (enterprise
identity, k8s scale, autoscaling/HA, infra memory, background LLM
watchers, AgentEval, white-label, multi-project) have nowhere to
land as paid without forking the binary or hiding behind
config anyone can flip. Issue #164 — binding — specifies an
**edition-gated** model with a **signed offline license key**, on
top of one codebase and one binary. This change builds the gate; it
does not ship the paid features themselves.

## What Changes

- New capability `editions`: a single `Features` + `Limits` registry
  as the source of truth for every paid feature/limit key; an
  open-coded tier model (Community rank 0, plus one or more paid
  tiers via an open string code + ordinal rank); a signed-offline
  license (`base64url(payload).base64url(ed25519-sig)`) verified
  in-process against an embedded public key — no phone-home.
- Four enforcement points behind attribute/DI wrappers, all calling
  one shared `EditionGate.EvaluateAsync`: host API
  (`[RequiresFeature]` / `.RequireFeature()` / `[EnforceLimit]`),
  module installers (`AddComukiModule<TModule>` / `[EditionFeature]`),
  background workers (`[RequiresFeature]` on `IComukiWorker`), and
  `GET /api/v1/edition` (tier, status, visible keys — dashboard/CLI
  hide or upsell without hand-copying the registry).
- New `Comuki.Shared.Editions` library (`EditionTier`, `LicenseKey`,
  `IEditionCapabilityRegistry`, `IEdition`, `ILicenseProvider`,
  `Features`/`Limits` catalogs) — Host composes it, every module
  installer/worker references it; no module owns "the editions
  module."
- New `tools/Comuki.Codegen.Editions` CLI (mirrors
  `Comuki.Codegen.Realtime`): renders a Markdown capability table +
  a typed TypeScript contract from the same registry, so keys are
  typed, not stringly. kubb still types the `/api/v1/edition`
  response shape; this emitter types the keys.
- Modified capability `host`: `GET /api/v1/edition` wired into host
  composition; `ProviderExceptionHandler` gets one new arm (reusing
  `ProviderForbiddenException` → 403) for
  `edition.feature_unavailable` / `edition.limit_exceeded`.
- License storage: `Host:License:Path` in `config.toml` via the
  existing `ISecretResolver`/`SecretRef` machinery; hot-reloaded via
  `IOptionsMonitor<LicenseOptions>`. Expiry: grace period, then
  read-only degrade — never data loss, never a boot refusal.

## Capabilities

### New Capabilities

- `editions`: registry, tier model, license format, four enforcement
  points, grace/read-only-degrade behaviour, hot reload, fixture
  licenses, three architecture-test invariants (registry ↔ attribute
  round-trip; every paid entry gated somewhere; Community DI graph
  `ValidateOnBuild`s clean).

### Modified Capabilities

- `host`: `GET /api/v1/edition` route + payload binding, and the two
  new exception-handler arms.

## Impact

Touches a new shared library (`Comuki.Shared.Editions`), a new
codegen CLI (`Comuki.Codegen.Editions`), one new
`capability-matrix.md` artifact, `HostComposer`, and
`ProviderExceptionHandler`'s mapping table. Edition gating is a
separate axis from permission gating (both apply to the same
endpoint via independent filters); no new permission key is added.

## Non-goals

- Implementing the paid features themselves (multi-repo, k8s scale,
  SSO/SCIM, audit export, infra memory, LLM watchers, AgentEval,
  white-label) — land in their own changes against this registry.
- Implementing worker-commit-attribution (#165) — depends on this
  change only for the gate mechanism.
- A payment/billing/issuance system — this specifies
  **verification** only; issuance tooling is out-of-tree.
- Phone-home of any kind — verification is 100% offline.
- A second binary/package or build-time edition switch — one
  codebase, one binary, one runtime gate.
- Dashboard/CLI **implementation** — the `/api/v1/edition` contract
  is in scope; hook/component bodies are follow-ups.
- Data loss on tier change — degrades to read-only, never deletes,
  never refuses to boot.
