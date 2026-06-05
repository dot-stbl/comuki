---
milestone: v1
status: phase-3-complete
last_updated: 2026-06-05
progress:
  total_phases: 8
  completed_phases: 3
  total_plans: 3
  completed_plans: 3
  percent: 37
---

# Project State

## Current Position

Phase: 3
Plan: 3 of 3
Status: phase_complete

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
Phase 3: Design System & Testing Infrastructure — **IN PROGRESS** (2026-06-05).
  - 3.1 Testing infra — **DONE** (2026-06-05).
    - BE: xUnit v3 (MTP runner via `dotnet run`), Shouldly, NSubstitute,
      Testcontainers.PostgreSql, Respawn, Bogus, coverlet.collector (70% gate)
    - BE: `tests/Comuki.Platform.Architecture.Tests/` (3 layer tests),
      `tests/Comuki.Platform.Testing/` (shared infra library, no [Fact]),
      `tests/Comuki.Platform.Orchestration.Unit.Lease/` (placeholder smoke test)
    - FE: vitest + testing-library + jsdom + @testing-library/user-event
    - FE: `vitest.config.ts` (jsdom, @/ alias, 70% v8 coverage threshold)
    - FE: `utils.test.ts` (4 tests on cn()), `vitest.setup.ts`
    - FE: Playwright (playwright.config.ts, e2e/landing.spec.ts 3 smoke tests)
    - CI: `test-backend` + `test-frontend` jobs in `.gitlab-ci.yml`
    - Key deviation: xUnit v3 uses `dotnet run` not `dotnet test` (MTP, not VSTest)
  - **3.2 Design tokens — **DONE** (2026-06-05).
    - IBM Plex Mono replaces Geist Mono Variable (`@fontsource/ibm-plex-mono`)
    - Slate-blue + cool-black palette: `#83A1DC`/`#15171B` dark, `#3C5A86`/`#FBFBFA` light
    - `--radius: 0.375rem` (6px); all 6 status tokens with per-theme hex values
    - Storybook backgrounds + `components.json` baseColor updated (`mauve` → `slate`)
  - 3 plans total (3.1 test infra ✓, 3.2 design tokens ✓, 3.3 stories + custom components ✓)
  - 3.3 Stories + 3 custom components — **DONE** (2026-06-05).
    - 3 custom components: `StatusBadge` (semantic pill with --st-* tokens), `RunIdChip` (copy-to-clipboard mono chip), `ModeToggle` (sun/moon/system switcher via local `useTheme`)
    - 58 Storybook stories (55 shadcn + 3 custom), all with 6 canonical states per frontend-construct-rules.md § 2
    - `@storybook/addon-vitest` + `@storybook/addon-a11y` deferred to Phase 7 (v10-only, project uses SB 8)
    - `bun run build-storybook` exit 0 ✓; `bun run test` exit 0 ✓
  - Slice 0 vertical slice moved to Phase 4 (was Phase 3 in the
    original plan).

## Goal (milestone v1)

Vertical slice through the platform (Slice 0 from `comuki-slice-0.md`): one ticket runs
end-to-end through a single worker — pull-claim, Translator/gRPC bridge, container
lifecycle. After that, Slice 1 (proxy) → 2 (knowledge) → 3 (verification) → 4 (DAG +
dashboard) → MVP polish.

## Progress

3 / 8 phases, 3 / 3 plans — 37 %

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
| 3-plan split for Phase 3 (3.1 test infra, 3.2 design tokens, 3.3 stories + custom components) | Token change must land before `StatusBadge` consumes `--st-*`; tests must land before CI can gate axe. Linear 3.1 → 3.2 → 3.3 sequencing, each is a single reviewable unit. | 3 |
| Architecture test project at `platform/tests/Comuki.Platform.Architecture.Tests/` (sibling to `platform/src/`, not inside it) | Per `comuki-project-structure.md` §2 the `tests/` folder is at the C# solution root, alongside `src/`. Lives under `/platform/tests/` in the slnx. | 3 |
| Edit `comuki.slnx` directly when adding test projects (do not use `dotnet sln add --solution-folder`) | The .NET 10 SDK on Windows collapses `--solution-folder` paths (decision 1 above). Editing `.slnx` is the established workaround; verify solution folder = physical path after. | 3 |
| Test project naming: `Comuki.Platform.<SrcProject>.<Kind>[.<Feature>]` per `TESTING-RULES.md § 10`; omit `.Feature` for first test of a src project (it'll be added if a second appears) | Matches PROJECT-STRUCTURE.md § 10 convention; only the architecture test gets a non-`<Kind>` suffix (`Architecture.Tests` is singular in the doc). | 3 |
| 70% line coverage gate on both BE and FE (per `TESTING-RULES.md § 10`, explicitly NOT 80%+) | 80%+ forces meaningless tests for the metric. 70% is the documented floor. Gate is wired in 3.1; the empty production code passes trivially and the gate becomes meaningful as features land. | 3 |
| Add `@storybook/addon-vitest` + `@storybook/addon-a11y` in plan 3.3, not 3.1 | 3.1 only sets up top-level vitest + Playwright + CI; the Storybook-testing addons only earn their keep when there are stories to test, which is 3.3. | 3 |
| `next-themes` stays as a dep (do not remove, do not migrate) | `dashboard/src/components/ui/sonner.tsx` imports `useTheme` from `next-themes` (shadcn-official, do not edit). The local `theme-provider.tsx` remains the canonical theme source; `ModeToggle` consumes the local one. | 3 |
| `ModeToggle` consumes the local `useTheme` from `theme-provider.tsx`, NOT the `next-themes` one | Locked decision: local provider is the contract; `next-themes` is only there for sonner. | 3 |
| `StatusBadge` lives in `dashboard/src/components/ui/status-badge.tsx` (next to shadcn) — NOT in a `dashboard/src/comuki/` subfolder | Locked decision: custom components sit alongside shadcn primitives in `ui/`. | 3 |
| Storybook story files for all 55 shadcn components (each with 6 canonical states) + 3 custom components | Locked decision: do not narrow to "top 10"; every primitive gets the full state coverage per `frontend-construct-rules.md § 2`. | 3 |
| Comuki palette: slate-blue accent (#83A1DC dark / #3C5A86 light) + cool-black surfaces (#15171B dark / #FBFBFA light); IBM Plex Mono everywhere; 6 status tokens; --radius: 0.375rem | From `.soly/docs/design-system/Comuki Design System.md` § 3 (source of truth); replaces shadcn's radix-mira + mauve defaults. The `failed` status uses terracotta (`#D6685A` dark / `#B0473B` light) as the danger color — that is the only non-neutral "loud" color. | 3 |
| xUnit v3 uses Microsoft Testing Platform (MTP), not VSTest | Tests run via `dotnet run --project <csproj>` not `dotnet test`. VSTest does not support xUnit v3 test discovery. | 3.1 |
| Storybook test addons (@storybook/addon-vitest, @storybook/addon-a11y) deferred to plan 3.3 | `@storybook/addon-vitest` preset has Node.js 24 compatibility issue (ERR_INTERNAL_ASSERTION on ES module loading). | 3.1 |
| `Comuki.Platform.Testing` (not `Acme.Shop.Testing` per template) | Project prefix matches actual codebase (`Comuki.Platform`), not the TESTING-RULES template name (`Acme.Shop`). | 3.1 |
| `@storybook/addon-vitest` + `@storybook/addon-a11y` are SB 10-only; project uses SB 8; deferred to Phase 7 | Both packages have no v8.x release; `addon-vitest` has Node.js 24 ESM loader bug. Both removed from `package.json`; TODO comments added to `main.ts`/`preview.ts`. | 3.3 |
