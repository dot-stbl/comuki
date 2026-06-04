# Roadmap

> 8 phases. Each phase is a coherent chunk of value; plans inside a phase may run in
> waves. Granularity chosen per request: "мельче" — each slice from `comuki-slice-0.md`
> gets its own phase, polish is its own phase.

## Phase 1 — Bootstrap (`01-bootstrap`)

**Goal:** polyglot monorepo skeleton with a compiling C# solution. No business code
yet — just enough that the next phase can land on solid ground.

**Scope**
- Top-level folders: `platform/`, `agents/`, `dashboard/`, `control-plane/`, `deploy/`
- `comuki.slnx` at repo root with 5 base projects (see `phases/01-bootstrap/CONTEXT.md`)
- `Directory.Build.props` (net10.0, warnings-as-errors, latest analyzers)
- `.gitignore`, initial `git init`
- `dotnet build` → 0 warnings, 0 errors

**Out of scope**
- TS packages in `agents/`, React app in `dashboard/`, `deploy/docker-compose.yml` —
  deferred to Phase 2 (stack foundation) once the C# graph is stable
- Tests project — added in the slice that needs it first
- EF Core / Npgsql wiring — added in Phase 3 (Slice 0 Step 1) when Postgres first
  appears

**Depends on:** —

## Phase 2 — Stack Foundation (`02-stack-foundation`)

**Goal:** concrete stack for the two remaining top-level areas. Decide tooling
("потом определятся со стеком") and commit it.

**Scope**
- `agents/comuki-agent-core` (TS package) — placeholder for shared event types
- `dashboard/` (React + Vite + shadcn) — empty app with a "Comuki" landing
- `deploy/docker-compose.yml` — self-hosted infra: postgres, minio, nexus, victoria
- `deploy/worker.Dockerfile` — pi + Translator skeleton (not yet run end-to-end)
- Centralized decision: package manager (bun vs pnpm), Node version, Vite vs Next,
  shadcn version, docker-compose compose-spec version
- CI scaffolding (build verification for both stacks)

**Out of scope**
- Real Translator / pi integration — that's Phase 3
- Real dashboard features — Phase 7

**Depends on:** Phase 1

## Phase 3 — Slice 0 Vertical Slice (`03-slice-0-vertical`)

**Goal:** one trivial ticket flows through the whole platform end-to-end. Per
`comuki-slice-0.md`: prove pull-model, Translator/gRPC, and pi-as-headless-agent.

**Scope** (5 sub-steps, 5 plans)
- **3.1** Sanity-check `pi` headless: launch pi manually with a trivial prompt, parse
  stream-json output, confirm Anthropic-compatible endpoint reachable (Step 0 of slice-0).
- **3.2** Postgres + claim primitive: `runs` / `tasks` tables, `FOR UPDATE SKIP LOCKED`
  claim with lease, two-claimer race test, lease reaper (Step 1 of slice-0).
- **3.3** Translator (C# AOT) launches pi, parses stream-json into typed events
  (`StageReport`, `StageActivity`), prints them. No gRPC yet (Step 2 of slice-0).
- **3.4** gRPC bidirectional stream between Translator and Orchestrator; orchestrator
  can send `Stop` (Step 3 of slice-0).
- **3.5** Container loop: insert task → spin container → Translator claims + runs pi on
  trivial brief → streams events → orchestrator logs final `StageReport` → container
  dies → lease released (Step 4 of slice-0).

**Out of scope**
- Proxy / virtual keys (Phase 4)
- Knowledge retrieval (Phase 5)
- Verification gate beyond "did pi report done" (Phase 6)
- DAG, multi-stage, real worktrees (Phase 7)

**Depends on:** Phase 2

## Phase 4 — Slice 1: Proxy & Virtual Keys (`04-slice-1-proxy`)

**Goal:** workers stop holding real model keys. The proxy mediates everything;
container knows only a virtual URL + capability-scoped key.

**Scope**
- `Comuki.Platform.Proxy` (ASP.NET Core + YARP) — thin pass-through
- `Comuki.Platform.Routing` feature — role→physical-model resolution table
- Virtual key format: signed, short-lived, carries route/budget/scope/TTL
- Secret-manager integration stub (real provider TBD by deployment)
- Egress allowlist
- Priority rule "people > swarm" enforced by the proxy
- Metricing: per-app, per-stage, per-agent cost attribution

**Out of scope**
- Qdrant vs pgvector — locked to pgvector for now (Phase 5 can re-evaluate)
- Per-key policies UI — Phase 7 dashboard

**Depends on:** Phase 3 (real worker traffic to route)

## Phase 5 — Slice 2: Knowledge & MCP (`05-slice-2-knowledge`)

**Goal:** `comuki-mcp` server exposes retrieval over MCP. Briefs are assembled by a
context manager, not inlined by humans.

**Scope**
- `Comuki.Platform.Knowledge` + `Comuki.Platform.Database.Knowledge` (pgvector)
- `Comuki.Platform.Mcp` — official C# MCP SDK, exposes retrieval
- Context manager in Orchestration: gathers repo map + relevant docs + convention
  digest for a brief
- Seed the knowledge base with project rules + design system + onboarding docs
- Doc ingestion path (manual for now; doc-agent automated in Phase 6+)

**Out of scope**
- Auto doc-update on feature merge — doc-agent in Phase 6
- Qdrant migration — only if pgvector chokes under load

**Depends on:** Phase 4

## Phase 6 — Slice 3: Verification Gate & Rules (`06-slice-3-verification`)

**Goal:** the platform refuses to merge work that didn't pass a deterministic gate.
Anti-slop hardening: hooks, not vibes.

**Scope**
- Verification stage: types / lint / unit / build (Roslyn warnings-as-errors are
  non-negotiable per architecture.md §01)
- `Comuki.Platform.Rules` — rule engine: scope (global / app / stage / task-type),
  version, conflict detection
- Worker-side enforcement: `comuki-worker-sdk` pi-extensions that block test-file
  edits, install commands, main-branch push
- Escalation policy: N failed retries on cheap model → switch to leading model
- Budget caps per task / per app / global + kill-switch
- Cost-per-successful-task metric (cheap retries can flip the economics)

**Out of scope**
- Visual review layer (Storybook + vision model) — defer until dashboard phase
- Playwright E2E — defer until UI exists

**Depends on:** Phase 5

## Phase 7 — Slice 4: DAG & Dashboard (`07-slice-4-workflow`)

**Goal:** multi-stage workflows with a contract-first seam and a live operations UI.

**Scope**
- DAG engine in Orchestration: stage types from a fixed catalogue (study, contract,
  backend, frontend, migrate, tests, deploy, doc), planner assembles from facts
  produced by the studier — not from ticket text
- OpenAPI contract as the first artifact of a backend-feature stage; front∥back
  on the contract, sync on real schema
- Preview environments: per-stage, schema-seeded (not data/secret-seeded); prod
  touched only by the last stage behind the gate
- `comuki-dashboard` operational UI per `comuki-dashboard-designspec.md`:
  intake, runs, approvals, trace, cost — React + shadcn, dark default
- SignalR real-time stream from Orchestrator to dashboard
- Human-in-the-loop approvals wired through the dashboard queue

**Out of scope**
- External tracker (Jira / Linear) integration — "далекое потом"
- Auto-deploy to prod without approval — explicit non-goal for v1
- Custom user auth on the dashboard — local-only for v1

**Depends on:** Phase 6

## Phase 8 — MVP Polish (`08-mvp-polish`)

**Goal:** the system survives its own mistakes. At-least-once durability, three-layer
observability, eviction of the worst "the gate passed but it's broken" cases.

**Scope**
- OTel instrumentation everywhere (business + tech metrics share one time-series)
- VictoriaMetrics + VictoriaLogs deploy
- Append-only event log: trace-id = run-id; the log is the debug interface
- Idempotency keys on dispatch / merge / deploy; "did I already do this?" check
  before every irreversible step
- Reaper for orphaned containers + reconciliation on Orchestrator restart
- Eval harness scaffolding: golden tasks derived from shipped skills
- Acceptance: 70% line coverage target, 0 critical/high risks unresolved
- Onboarding doc: how a new dev runs the whole loop on a fresh checkout

**Out of scope**
- Temporal upgrade — Postgres-only was a deliberate MVP choice; revisit only if
  durable logic outgrows what we can hold in one DB transaction
- Public product design system — lives in the product repo, not here

**Depends on:** Phase 7

---

## Phase graph

```
1 Bootstrap ──► 2 Stack Foundation ──► 3 Slice 0 (3.1→3.2→3.3→3.4→3.5)
                                              │
                                              ▼
                                         4 Slice 1 Proxy
                                              │
                                              ▼
                                         5 Slice 2 Knowledge & MCP
                                              │
                                              ▼
                                         6 Slice 3 Verification & Rules
                                              │
                                              ▼
                                         7 Slice 4 DAG & Dashboard
                                              │
                                              ▼
                                         8 MVP Polish
```
