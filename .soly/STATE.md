---
milestone: v1
status: phase-2-pending
last_updated: 2026-06-04
progress:
  total_phases: 8
  completed_phases: 1
  total_plans: 0
  completed_plans: 0
  percent: 12
---

# Project State

## Current Position

Phase: 3
Plan: — of —
Status: waiting on user (design-system docs)

## Active Phase

Phase 1: Bootstrap — **DONE** (2026-06-04).
  - `dotnet build comuki.slnx` → 0 warnings, 0 errors.
  - `GET /health` → 200 `{"status":"ok"}`.
Phase 2: Stack Foundation — **DONE** (2026-06-04).
  - bun + Vite 8 + React 19 + TS strict + Tailwind v4
  - shadcn/ui (56 components, Radix base) + Storybook 8.6
  - BE: OpenAPI runtime + Scalar + build-time codegen via
    `Microsoft.Extensions.ApiDescription.Server` (openapi-v1.json
    written next to the .csproj; Kubb reads it; single source of
    truth for FE and any future SDK)
  - GitLab CI (.gitlab-ci.yml, 2 jobs)
  - deploy/ for local dev (postgres+pgvector, minio, nexus, victoria)
  - `agents/` + `control-plane/` directory skeletons (real TS
    packages land in Phase 4)
Phase 3: Design System & Testing Infrastructure — **WAITING**.
  - User is finishing `docs/design-system/*.md` (tokens,
    components, patterns, voice).
  - When they land: `soly plan 3` emits 3 plans
    (3.1 test infra, 3.2 tokens, 3.3 stories+components).
  - Slice 0 vertical slice moved to Phase 4 (was Phase 3 in the
    original plan).

## Goal (milestone v1)

Vertical slice through the platform (Slice 0 from `comuki-slice-0.md`): one ticket runs
end-to-end through a single worker — pull-claim, Translator/gRPC bridge, container
lifecycle. After that, Slice 1 (proxy) → 2 (knowledge) → 3 (verification) → 4 (DAG +
dashboard) → MVP polish.

## Progress

1 / 8 phases, 0 / 0 plans — 12 %

## Decisions

| Decision | Rationale | Phase |
|----------|-----------|-------|
| Use **Conventional Commits 1.0.0** in this repo — no `[stbl]` prefix. See `.soly/rules/process/commit-format.md`. | The `[stbl]` prefix from `~/.claude/rules/git.md` is an anlytra-project convention, not a comuki one. The user (this is a hybrid repo) prefers Conventional Commits here. Per-rule hierarchy: `.soly/rules/` overrides `.claude/rules/`. | 1 |
| Apply commit-format rule **forward only** — do not rewrite the 3 pre-rule commits (`229d1dc`, `e36bda4`, `1be5302`). | History rewriting for cosmetic reasons is not worth the risk. New commits are in scope; old ones stay as-is and document the bootstrap era. | 1 |
| Polyglot monorepo split by stack (`platform/` C#, `agents/` TS, `dashboard/` React) | Per `comuki-project-structure.md` §1: each stack keeps its own manifest, lockfile, toolchain. | 1 |
| `comuki.slnx` at repo root, not inside `platform/` | Root entry matches the polyglot layout; future `agents.sln` / `dashboard` workspaces stay siblings, not nested. | 1 |
| Phase 1 ships 5 of 17 projects (Api.Public, Orchestration, Entity.Core, Api.Contracts, Database.Runs) | Minimal compile graph; the other 12 land in the slice that first needs them (Translator, Proxy, Knowledge, …). | 1 |
| `net10.0` target, `TreatWarningsAsErrors=true`, analyzers at `latest` | Per architecture.md §01 — verification is a load-bearing wall; Roslyn warnings-as-errors are non-negotiable. | 1 |
| `Directory.Build.props` at repo root, not inside `platform/` | One place to tune C# defaults; future non-C# stacks (TS, React) live in their own folders and won't be touched. | 1 |
| `.editorconfig` already on disk, supplied by the user | Code below must respect the existing rules — `dotnet build` will fail loudly otherwise. | 1 |
| `.soly/rules/` (C# style/framework/testing) carries over as-is | These rules are explicitly referenced by `comuki-project-structure.md` (`PROJECT-RULES.md`); they apply to the `platform/` solution unchanged. | 1 |
| Soly state files (STATE.md, ROADMAP.md, phases/) are committed, not gitignored | Next developer / next soly session must see the same context; only the runtime cache `rule-mtimes.json` is gitignored. | 1 |
| Two path mistakes in initial csproj (3 `..\` levels instead of 2) | Self-inflicted; both `Orchestration.csproj` and `Database.Runs.csproj` live in `src/feature/` and `src/database/`, not nested. Fixed in same commit. | 1 |
| `NoOpOrchestrationService` co-located in `IOrchestrationService.cs`, not in `Program.cs` | Trying to define a placeholder implementation in `Program.cs` hit IDE0065 (`using` inside namespace) — co-locating the marker class with its interface is cleaner. | 1 |
| `comuki.slnx` rewritten with `<Folder>` elements matching physical paths | PROJECT-STRUCTURE.md §7 requires `Solution folder = physical path`. Flat list was a rule violation caught on user review. | 1 |
| `dotnet sln add --solution-folder` collapsed all projects on .NET 10 SDK (Windows path quirk) — wrote `.slnx` directly instead | `dotnet sln` and `--solution-folder` is the canonical path, but the .NET 10 SDK mishandles `path-with-slashes` on Windows. Bootstrap is an exception to the "руками не редактируем" rule. Future projects: still use `dotnet sln add --solution-folder` with a `.slnx` re-check. | 1 |
