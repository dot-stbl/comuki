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

Phase: 2
Plan: — of —
Status: ready to plan

## Active Phase

Phase 1: Bootstrap — **DONE** (2026-06-04).
  - `dotnet build comuki.slnx` → 0 warnings, 0 errors.
  - `GET /health` → 200 `{"status":"ok"}`.
  - Initial commit `chore: bootstrap monorepo skeleton`.
Phase 2: Stack Foundation — next. Pick TS package manager, React stack (Vite+shadcn),
`deploy/docker-compose.yml` (postgres+minio+nexus+victoria), CI scaffolding.

## Goal (milestone v1)

## Goal (milestone v1)

Vertical slice through the platform (Slice 0 from `comuki-slice-0.md`): one ticket runs
end-to-end through a single worker — pull-claim, Translator/gRPC bridge, container
lifecycle. After that, Slice 1 (proxy) → 2 (knowledge) → 3 (verification) → 4 (DAG +
dashboard) → MVP polish.

## Progress

0 / 8 phases, 0 / 0 plans — 0 %

## Decisions

| Decision | Rationale | Phase |
|----------|-----------|-------|
| Polyglot monorepo split by stack (`platform/` C#, `agents/` TS, `dashboard/` React) | Per `comuki-project-structure.md` §1: each stack keeps its own manifest, lockfile, toolchain. | 1 |
| `comuki.slnx` at repo root, not inside `platform/` | Root entry matches the polyglot layout; future `agents.sln` / `dashboard` workspaces stay siblings, not nested. | 1 |
| Phase 1 ships 5 of 17 projects (Api.Public, Orchestration, Entity.Core, Api.Contracts, Database.Runs) | Minimal compile graph; the other 12 land in the slice that first needs them (Translator, Proxy, Knowledge, …). | 1 |
| `net10.0` target, `TreatWarningsAsErrors=true`, analyzers at `latest` | Per architecture.md §01 — verification is a load-bearing wall; Roslyn warnings-as-errors are non-negotiable. | 1 |
| `Directory.Build.props` at repo root, not inside `platform/` | One place to tune C# defaults; future non-C# stacks (TS, React) live in their own folders and won't be touched. | 1 |
| .editorconfig already on disk, supplied by the user | Code below must respect the existing rules — `dotnet build` will fail loudly otherwise. | 1 |
| `.soly/rules/` (C# style/framework/testing) carries over as-is | These rules are explicitly referenced by `comuki-project-structure.md` (`PROJECT-RULES.md`); they apply to the `platform/` solution unchanged. | 1 |
| Soly state files (STATE.md, ROADMAP.md, phases/) are committed, not gitignored | Next developer / next soly session must see the same context; only the runtime cache `rule-mtimes.json` is gitignored. | 1 |
| `dotnet build comuki.slnx` produced 0 warnings/0 errors on first try after fixing two path mistakes (3 `..\` levels instead of 2 in two csproj) | Self-inflicted; both `Orchestration.csproj` and `Database.Runs.csproj` live in `src/feature/` and `src/database/`, not `src/feature/Comuki.Platform.Orchestration/`. Fixed in-place; no scope change. | 1 |
| `Program.cs` uses `using` directives + `MapGet` minimal-API style, with `NoOpOrchestrationService` co-located in `IOrchestrationService.cs` | Trying to define a placeholder implementation in `Program.cs` hit IDE0065 (`using` inside namespace) — co-locating the marker class with its interface is cleaner and avoids the issue. | 1 |
| `comuki.slnx` rewritten with `<Folder>` elements matching physical paths, not a flat list. `dotnet sln add --solution-folder` was tried first but on .NET 10 SDK it collapsed all projects into a single folder on Windows — known path-handling quirk. Bootstrap commits are an exception to the "руками не редактируем" rule. Future projects: still use `dotnet sln add --solution-folder` and re-check `.slnx`; fall back to `write` if the same bug recurs. | PROJECT-STRUCTURE.md §7 requires `Solution folder = physical path`. Flat list was a rule violation caught by user on review. | 1 |
