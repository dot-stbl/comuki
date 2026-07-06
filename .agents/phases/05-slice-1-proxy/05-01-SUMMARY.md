---
plan: 05-01
completed: 2026-06-23
duration: ~1 day (urgent)
files-touched: 26 added + docs
---

# Summary — Z.AI key rotation proxy

## What shipped

First vertical cut of Phase 5 (Slice 1 — Proxy). A thin YARP host
transparently rotates exhausted Z.AI keys so the worker agent (`pi`)
keeps running without seeing quota failures.

- **`Comuki.Platform.Routing`** (`feature/`) — the logic, 4 units:
  - `KeyPool` — thread-safe in-memory pool with per-key cooldown
    (`TryAcquire` / `MarkExhausted`, `TimeProvider`-driven).
  - `QuotaExhaustionDetector` — pure function over configurable rules
    (status + body-contains, case-insensitive).
  - `KeyRotatingForwarder` — retry orchestrator: buffer request body,
    loop over the live pool, mark-exhausted + rotate on quota signal,
    503 + Anthropic-compatible body when all keys are down.
  - `YarpUpstreamSender` + `RotatingTransformer` — the YARP
    `IHttpForwarder` glue (key swap on the outbound request, inspect
    the upstream response before any byte reaches the client).
- **`Comuki.Platform.Proxy`** (`application/internal/`) — thin host:
  Kestrel, `/health`, catch-all → forwarder. No business logic.
- **Tests** — `Routing.Unit.KeyRotation` (18) + `Proxy.Integration.Rotation`
  (2, `WebApplicationFactory` + fake Z.AI Kestrel stub). All green.

## Deviations from plan

- Plan body said buffer the whole request body unbounded; shipped with
  a bounded `RequestBufferThresholdBytes` option (default 1 MB, spills
  to temp file via `EnableBuffering`) — added in `7d66ee6`.
- `Retry-After` parsing uses injected `TimeProvider` (not
  `DateTimeOffset.UtcNow`) — added in `7d66ee6`, required by analyzers.
- **Out-of-scope-but-fixed post-hoc (`dc38be2`):** the urgent run only
  executed `dotnet build`, skipping the `dotnet format` gate (36 IDE
  violations). Fixed: dropped redundant `this.`, camelCased private
  fields, applied the Pyramid Rule to 3 primary ctors, rewrote tests to
  BDD, added a `[tests/**.cs]` editorconfig exemption for the async-suffix
  rule (matches TESTING-RULES §3 BDD examples).

## Verification

- `dotnet build comuki.slnx -p:EnforceExtendedAnalyzerRules=true` → 0/0.
- `dotnet format comuki.slnx --verify-no-changes --severity warn` → exit 0.
- `dotnet run --project …Routing.Unit.KeyRotation` → 18/18.
- `dotnet run --project …Proxy.Integration.Rotation` → 2/2 (rotation
  observed live: `429 → key_rotated → 200`).

## Process note

This plan was executed via the `superpowers` subagent workflow, **not**
soly — that's why STATE.md didn't advance past Phase 3 on its own. The
plan + design were relocated from `docs/superpowers/` into this phase
folder and given soly frontmatter post-hoc.

## Next (rest of Phase 5 — out of scope for this cut)

Virtual keys (signed, TTL, capability/scope), role→model routing table,
metering + cost attribution, budgets + kill-switch, egress allowlist,
secret-manager (Vault) integration, fallback to alt provider. See
`05-01-DESIGN.md` § "Вне объёма".
