# Roadmap

> **Status (2026-09-04): v1 milestone is complete on master (`2bb2afd`).**
> 24 slices landed — 15 original v1 core (S0–S14) plus 9 follow-on slices
> (5 FE wire-up slices, 2 polish waves, 1 admin endpoints, 1 docs sweep).
> 28 of 41 GitHub issues are closed; the 13 open issues are post-v1
> scope (`#11` post-1.0 backlog, `#31–#42` FE admin mutations wire-up,
> `#43` artifacts e2e test cleanup). Live status lives in
> [`.agents/STATE.md`](./STATE.md) and on
> https://github.com/dot-stbl/comuki/issues.
>
> **Phase 3 was re-scoped** from the original Slice 0 vertical slice to
> "design system + testing infrastructure" — they're load-bearing before
> any runtime ships (anti-slop contract for Phase 6+, quality gate for
> every later phase). Slice 0 moved to Phase 4. The numbering after Slice 0
> also shifts +1.

## Slice ↔ phase index

| Slice | Issue | Phase  | Title |
|-------|-------|--------|-------|
| S0    | #1    | 4 / 5  | Скелет platform/: shared · modules · engine · host |
| S1    | #2    | 4 / 5  | Runs · очередь · journal |
| S2    | #3    | 5      | Compute: Docker provider + scale v0 |
| S3    | #4    | 4      | Translator · worker image · gRPC (Slice 0 e2e) |
| S4    | #12   | 5      | Identity: users · API keys · RBAC · OIDC |
| S5    | #5    | 6 / 7  | Chat (Voluta в Host) + Host.Brain + approve + cancel |
| S6    | #6    | 8      | Intake: GH · GL · Yandex Tracker · Jira + sync-back + PR-review |
| S7    | #7    | 8      | FE ядро + SignalR realtime (kubb-client + 5 wire-up slices) |
| —     | #13   | 9      | Compute k8s + quotas + observability (closed in Wave 6) |
| S9    | #8    | 9      | Cross-cutting kit + cost/budgets + optional proxy |
| S10   | #9    | 6      | Knowledge (opt-in): pgvector · MCP · docs worker |
| S11   | #10   | 9      | v1.0 polish: security · load · onboarding · docs sync |
| S12   | #14   | 4 / 8  | agents/ TS-пакеты: agent-core · worker-sdk · dev-sdk |
| S13   | #15   | 8      | control-plane дефолты: профили · каталог · chat-commands |
| S14   | #16   | 9      | CI: GitHub Actions |

**Follow-on v1 slices (post-original 15):**

| Slice          | Issues         | Title |
|----------------|----------------|-------|
| wave-6 polish  | #17–#25        | IExceptionHandler, schema-per-DbContext, folder cap, Dto suffix drop, costs permissions, dev-secret removal, etc. |
| artifacts      | #28            | MinIO run-artifact bundle (`brief/result/pins`) + bucket auto-init |
| openapi-emit   | #29            | Build-time OpenAPI emission + kubb alignment |
| pr-review      | #27            | Inbound GH/GL pull-request admission + pr-review profile |
| fe-wire-runs   | slice 1        | kubb-client transport + runs queries + mappers |
| fe-wire-identity | slice 2      | login/me/oidc + kubb client + mappers |
| fe-wire-projects | slice 3      | projects queries/mutations wired |
| fe-wire-inbox  | slice 4        | inbox queries/mutations wired |
| fe-wire-oidc   | slice 5        | browser-driven OIDC start + callback |
| admin-endpoints | #31–#42 (BE) | 12 host endpoints (identity invite/grant/revoke/link/key + sources connect/update/probe/test) + tests + openspec requirements |

## Phase 1 — Bootstrap (`01-bootstrap`) — ✅ DONE

**Goal:** polyglot monorepo skeleton with a compiling C# solution.
No business code yet — just enough that the next phase can land
on solid ground.

**Scope**
- Top-level folders: `platform/`, `agents/`, `dashboard/`, `control-plane/`, `deploy/`
- `comuki.slnx` at repo root with 5 base projects
- `Directory.Build.props` (net10.0, warnings-as-errors, latest analyzers)
- `.gitignore`, initial `git init`
- `dotnet build` → 0 warnings, 0 errors

**Out of scope** — TS packages, React app, docker-compose, EF Core
wiring, test projects, CI.

**Depends on:** —

## Phase 2 — Stack Foundation (`02-stack-foundation`) — ✅ DONE

**Goal:** commit the concrete stack for `agents/` (TS), `dashboard/`
(React), `deploy/` (Docker), and CI.

**Scope**
- BE: `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` +
  `Microsoft.Extensions.ApiDescription.Server` for build-time
  OpenAPI codegen
- FE: `bun` workspace, `Vite 8` + `React 19` + `TypeScript strict`
- FE: `Tailwind v4` via `@tailwindcss/vite`
- FE: shadcn/ui (Radix UI base) — 56 components, all installed
- FE: `Kubb` → typed React Query hooks from `openapi-v1.json`
- FE: `Storybook 8.6` (dark default, theme toggle)
- `deploy/docker-compose.yml` — postgres+pgvector, minio, nexus,
  victoria-metrics, victoria-logs
- `deploy/worker.Dockerfile` — sentinel for Phase 3 Slice 0
- `.gitlab-ci.yml` — 2 jobs (build-be, build-fe) + cache

**Out of scope** — real worker image, real test projects (Phase 3),
actual MCP client (Phase 5).

**Depends on:** Phase 1

## Phase 3 — Design System & Testing Infrastructure (`03-design-system`) — ✅ DONE

> **Re-scoped.** Was originally "Slice 0 vertical slice" but the
> design system + testing infra are load-bearing before any runtime
> — they form the contract agents get told to follow (Phase 6+
> anti-slop) and the gate that holds quality (Phase 6 verification).
> Pushing them in front of the vertical slice means every later
> phase ships against a real test suite and a real visual contract.

**Scope**
- **3.1 — Testing infrastructure (BE + FE):** xUnit v3 + Shouldly +
  NSubstitute + Testcontainers + Respawn + Bogus + coverlet (70%
  line gate) on BE; Vitest + Testing Library + jsdom + Playwright
  config (placeholder) on FE; `test-be` + `test-fe` jobs added to
  GitLab CI; NetArchTest layer enforcer. ✅ **DONE** (2026-06-05).
- **3.2 — Design tokens:** CSS variables + Tailwind v4 theme from
  `.agents/docs/design-system/Comuki Design System.md`; replace shadcn
  defaults (`radix-mira` + `mauve`) with Comuki's slate-blue accent
  (`#83A1DC` dark / `#3C5A86` light) + cool-black surfaces
  (`#15171B` / `#FBFBFA`); IBM Plex Mono everywhere; status tokens
  (`--st-running`, `--st-success`, `--st-failed`, `--st-waiting`,
  `--st-queued`, `--st-escalated`); theme-provider already wired.
  ✅ **DONE** (2026-06-05).
- **3.3 — Design system stories + component customization:**
  Storybook stories for every token (palette, type, radius, status
  semantics); stories for **all 55 shadcn/ui components** in
  `src/components/ui/` (each with Default, Loading, Disabled, Error,
  Empty, WithLongText states); three Comuki-specific custom
  components — `StatusBadge` (with `<StatusBadge status="running" />`
  API), `RunIdChip` (mono chip with copy-to-clipboard), `ModeToggle`
  (sun/moon/system theme switcher using existing
  `theme-provider.tsx`); Storybook interaction tests
  (`@storybook/test`); browser-mode component tests via
  `@storybook/addon-vitest` + `@storybook/addon-a11y` (axe); Playwright
  smoke test (boots landing page, asserts h1 contains "Comuki").
  ✅ **DONE** (2026-06-05).
  - Deviation: `@storybook/addon-vitest` + `@storybook/addon-a11y` are
    SB 10-only (this project uses SB 8); deferred to Phase 7.

**Out of scope** — worker agents actually using the design system
(Phase 6+), real visual-regression baselines (Phase 7).

**Depends on:** Phase 2; `docs/design-system/tokens.md` from user.

## Phase 4 — Slice 0 Vertical Slice (`04-slice-0-vertical`) — ✅ DONE (S3)

**Goal:** one trivial ticket flows through the system end-to-end.
Per `comuki-slice-0.md`: prove pull-model, Translator/gRPC, and
pi-as-headless-agent.

**Scope** (5 sub-steps, 5 plans)
- **4.1** Sanity-check `pi` headless: launch pi manually with a
  trivial prompt, parse stream-json output, confirm Anthropic-
  compatible endpoint reachable (Step 0 of slice-0). ✅
- **4.2** Postgres + claim primitive: `runs` / `tasks` tables,
  `FOR UPDATE SKIP LOCKED` claim with lease, two-claimer race test,
  lease reaper (Step 1 of slice-0). ✅
- **4.3** Translator (C# AOT) launches pi, parses stream-json into
  typed events (`StageReport`, `StageActivity`), prints them. No
  gRPC yet (Step 2 of slice-0). ✅
- **4.4** gRPC bidirectional stream between Translator and
  Orchestrator; orchestrator can send `Stop` (Step 3 of slice-0). ✅
- **4.5** Container loop: insert task → spin container → Translator
  claims + runs pi on trivial brief → streams events → orchestrator
  logs final `StageReport` → container dies → lease released
  (Step 4 of slice-0). ✅

**Out of scope** — proxy / virtual keys (Phase 5), knowledge
retrieval (Phase 6), real verification (Phase 7), DAG, multi-stage,
real worktrees (Phase 8).

**Depends on:** Phase 3.

## Phase 5 — Slice 1: Proxy & Virtual Keys (`05-slice-1-proxy`) — ✅ DONE (S2 + S4 + S12 + S9 T9.6)

**Goal:** workers stop holding real model keys. Everything through
`Comuki.Modules.Proxy` + `Comuki.Host.Proxy` on YARP; container
knows only virtual URL + capability-scoped key.

**Scope**
- `Comuki.Modules.Proxy` (resolver, store, extractors, budget, meter) ✅
- `Comuki.Host.Proxy` — YARP OpenAI/Anthropic passthrough ✅
- Virtual-key HMAC (models/budget/expiry); metering → `usage_events` ✅
- Cost-per-app, per-stage, per-agent metrics ✅
- Health probes for proxy virtual keys ✅
- Optional: `Proxy:Enabled=false` → proxy off, Translator talks
  upstream directly ✅

**Depends on:** Phase 4 (real worker traffic to route).

## Phase 6 — Slice 2: Knowledge & MCP (`06-slice-2-knowledge`) — ✅ DONE (S5 + S10)

**Goal:** `comuki-mcp` server exposes retrieval over MCP. Briefs
assembled by context manager, not inline.

**Scope**
- `Comuki.Modules.Knowledge` + `Comuki.Modules.Knowledge.Infrastructure`
  (pgvector schema, embedder/chunker/ingestor/searcher) ✅
- `Comuki.Modules.Memory` pgvector-backed (`SourceDocument` +
  `MemoryEmbedding`) — issue #26 schemas + #9 entities ✅
- `/api/v1/mcp` JSON-RPC 2.0 endpoint with tools `search_knowledge`
  + `list_runs` ✅
- `/api/v1/knowledge/ingest` behind `knowledge:write` permission ✅
- `comuki-agent-core` (TS) — MCP client + types (S12 #14) ✅

**Depends on:** Phase 5.

## Phase 7 — Slice 3: Verification Gate & Rules (`07-slice-3-verification`) — ✅ DONE

**Goal:** gate refuses to merge work that didn't pass deterministic
checks. Anti-slop hardening.

**Scope**
- Verification stage: types / lint / unit / build (warnings-as-errors) ✅
- `Comuki.Platform.Rules` — rule engine: scope, versioning, conflicts ✅
- `comuki-worker-sdk` — pi-extensions blocking test edits, install, push ✅
- Escalation policy: N failed retries → leading model ✅
- Budget caps + kill-switch ✅
- Cost-per-successful-task metric ✅

**Depends on:** Phase 6.

## Phase 8 — Slice 4: DAG & Dashboard (`08-slice-4-workflow`) — ✅ DONE (S6 + S7 + S13 + admin-endpoints)

**Goal:** multi-stage workflows with contract-first seam and live
operations UI.

**Scope**
- DAG engine in Orchestration — done (WorkItem + WorkItemDependency) ✅
- OpenAPI contract as the first artifact; front∥back, sync on real schema ✅
  (build-time emission `#29`, kubb v4.39.2, console.x pattern)
- Per-stage environments (schema-seeded, not data/secret-seeded);
  prod touched only behind the gate ✅
- `comuki-dashboard` — operational UI per
  `comuki-dashboard-designspec.md` (intake, runs, approvals, trace,
  cost) with all Comuki-specific components from Phase 3 ✅
  - 5 domains wired to real backend: runs · identity (session) ·
    projects · inbox · OIDC start
  - Mock-first follow-up: identity admin mutations + sources admin
    pages (`#31–#42`), tasks · models · observability · verify ·
    cost · compute · knowledge · queue · approvals · settings · home
    (post-v1)
- SignalR real-time stream ✅
- Worker rules in `control-plane/`, git-ref pinned per run ✅
- Admin endpoints BE (12 endpoints, issues #31–#42): identity
  invite/grant/revoke/link/key + sources connect/update/probe/
  test-draft/test-connection ✅

**Depends on:** Phase 7.

## Phase 9 — MVP Polish (`09-mvp-polish`) — ✅ DONE (S14 + S11 + S9 + housekeeping)

**Goal:** the system survives its own mistakes.

**Scope**
- OTel instrumentation everywhere; VictoriaMetrics + VictoriaLogs ✅
- Append-only event log: trace-id = run-id ✅
- Idempotency keys on dispatch / merge / deploy ✅
- Reaper for orphaned containers + reconciliation on restart ✅
- Eval harness scaffolding: golden tasks from shipped skills ✅
- 70% line coverage target across BE + FE ✅
- Onboarding doc: new dev runs the loop on a fresh checkout in ≤30 min ✅
- Security review: dev-secret removal (#21), per-endpoint rate-limits +
  CORS + prod-secret fail-fast (#10 T11.4)
- Load test: k6 scenarios + README (#10 T11.5)
- OIDC state sweep worker (5-min interval, configurable TTL)
- Host health probes for Postgres + proxy virtual keys
- Run approve + cancel endpoints (`b92d070`)
- `IExceptionHandler` registered at composition root (#17)
- Centralized ProblemDetails across 6 endpoints (#20)
- Per-DbContext Postgres schemas (#26) — 9 schemas now
- Folder cap enforced project-wide (#25)

**Depends on:** Phase 8.

## Phase graph

```
1 Bootstrap ──► 2 Stack Foundation ──► 3 Design System & Testing
                                              │
                                              ▼
                                         4 Slice 0 Vertical Slice
                                              │
                                              ▼
                                         5 Slice 1 Proxy & Virtual Keys
                                              │
                                              ▼
                                         6 Slice 2 Knowledge & MCP
                                              │
                                              ▼
                                         7 Slice 3 Verification & Rules
                                              │
                                              ▼
                                         8 Slice 4 DAG & Dashboard
                                              │
                                              ▼
                                         9 MVP Polish                  (done)
```

## Open slice work (post-v1 scope)

| # | Title | What's left |
|---|-------|-------------|
| 11 | Post-1.0 backlog | verify v1.1 · fleet · autonomy · merge-queue · domain-user · eval · Redis cache при multi-replica Host. |
| 31–#42 | FE admin mutations wire-up | Backend endpoints live (12 endpoints landed). Dashboard mutations (identity invite/grant/revoke/link/key/disable + sources connect/update/probe/test-draft/test-connection) still mock-first; kubb clients land per-issue. |
| 43 | Artifacts e2e test cleanup | Drop dead `postgresSeed` container from `ArtifactsEndToEndShould` (post-`8825387`). |

## Related

- [STATE.md](./STATE.md) — current state, slice cadence, decisions.
- [docs/architecture/](./architecture/) — design artifacts (decisions,
  architecture, stack, project structure).
- [docs/operations/](./operations/) — install, storage, OIDC, MinIO,
  OpenAPI codegen, database schemas, FE settings.