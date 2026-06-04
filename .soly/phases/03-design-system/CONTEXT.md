# Phase 3 — Design System & Testing Infrastructure

> **Re-scoped from the original "Slice 0 vertical slice" plan.**
> Reasoning: design system and testing infrastructure are **load-bearing
> before** runtime — they form the contract agents get told to follow
> (anti-slop, Phase 6+) and the gate that holds the line on quality
> (Phase 6 verification). Pushing them in front of the vertical slice
> means every later phase ships against a real test suite and a real
> visual contract, not ad-hoc ones.

## Цель

1. **Testing infrastructure** that both `platform/` and `dashboard/`
   run in CI on every commit.
2. **Design system** — tokens, components, patterns — baked into
   `dashboard/` from `docs/design-system/*.md` and protected by
   Storybook visual tests.

## Что входит

### 3.1 — Testing infrastructure (BE + FE)
Per `.soly/rules/coding/TESTING-RULES.md` (already in rules):

**BE (`platform/`):**
- `xunit.v3` + `Shouldly` + `NSubstitute` per test pyramid
- `Testcontainers` for integration (Postgres, Redis, …) — real
  containers in CI, not in-memory mocks
- `Respawn` for DB cleanup between tests
- `Bogus` for realistic test data
- `coverlet.collector` for coverage (target 70% line, per
  TESTING-RULES §10)
- One Unit test project per `feature/*` (e.g.
  `Comuki.Platform.Orchestration.Unit.Lease`) and one Integration
  per database project (e.g.
  `Comuki.Platform.Database.Runs.Integration.Migrations`)
- `Comuki.Platform.Architecture.Tests` — NetArchTest layer-rule
  enforcer (controllers only depend on services, models only
  depend on shared, etc.)

**FE (`dashboard/`):**
- `vitest` + `@testing-library/react` + `jsdom` for unit/component
- `playwright` config (deferred runtime until Phase 7 when there's
  something to test) — just the install + config + a placeholder
  test that boots the landing page
- `vitest --coverage` (v8 provider) wired to `bun run test`
- Coverage threshold 70% (mirrors BE)

**CI step (`.gitlab-ci.yml`):**
- `test-backend` and `test-frontend` jobs added after the existing
  `build-*` jobs
- Coverage artifacts uploaded (GitLab `cobertura` formatter)

### 3.2 — Design tokens
From `docs/design-system/tokens.md` (when user lands them):
- CSS variables in `dashboard/src/index.css` (`@theme inline { --color-bg: #1A1815; --color-accent: #C75D43; … }`)
- Tailwind v4 theme mapped to those variables
- Replace shadcn defaults (currently `radix-mira` + `mauve` base
  color) with Comuki's accent/terracotta + warm-black surfaces
- Light theme per `comuki-dashboard-designspec.md` § 3 light variant

### 3.3 — Design system stories + component customization
- Storybook story per design token (palette, typography, radius)
  — visual smoke test, "did the tokens land?"
- Per-component stories for the Comuki-specific components that
  shadcn doesn't ship: `StatusBadge`, `RunIdChip`, `CostTag`,
  `ModelBadge`, `DurationLabel`, `RiskTag`, `VersionRef`,
  `RunCard`, `StagePipeline`, `LiveRunsBoard`, `WorkerIndicator`,
  `RunTimeline`, `BriefViewer`, `ApprovalCard` (per
  `comuki-dashboard-designspec.md` § 4–5)
- Status colors with semantic names baked into the
  `StatusBadge` component (`<StatusBadge status="running" />`)
- Storybook interaction tests (`@storybook/test`) for clickable
  primitives
- Snapshot/visual regression scaffolding (Chromatic or reg-suit
  TBD — install + configure, real runs in Phase 7)

## Что НЕ входит

- **Worker agents actually using the design system** — that's
  Phase 6+ (anti-slop) when the rules engine + MCP surface read
  `docs/design-system/` and the worker-sdk enforces deviations.
- **Real visual-regression baselines** — first batch lands in
  Phase 7 when the dashboard has real pages.
- **Playwright E2E** beyond the placeholder — pages don't exist
  yet. Playwright config + one smoke test (boots the landing
  page, asserts h1 contains "Comuki") lands here as scaffolding.

## Зависит от

- Phase 2 (Stack Foundation) — done
- `docs/design-system/tokens.md` from user (drives 3.2 + 3.3)

## Definition of Done

1. `dotnet test` (or `dotnet test comuki.slnx`) runs from CI clean
   with 70% line coverage gate.
2. `bun run test` runs from CI clean with 70% line coverage gate.
3. `bun run build-storybook` produces a static export that
   shows every design token at least once (palette swatch, type
   ramp, radius scale, status colors).
4. `docs/design-system/` committed and matches the dashboard
   source (lint rule or visual diff catches drift in CI).
5. `playwright.config.ts` + at least one smoke test committed
   and passing.

## Hard constraints

- **xUnit v3 only** for BE (per `TESTING-RULES.md`); no Moq, no
  MSTest, no NUnit.
- **Vitest, not Jest** for FE — same reason.
- **Real containers, not in-memory** for BE integration — EF
  Core InMemory has different SQL semantics (case sensitivity,
  ordering) than real Postgres, so bugs it can't find are real
  ones, and bugs it does find are usually noise.
- **No `// @ts-ignore`** in FE test code either.
- **Coverage 70%**, not 80%+ — 80%+ forces meaningless tests
  for the metric.
- **Design tokens are the contract** — no per-component hex
  values, no inline `style={{ color: '#…' }}`, lint catches it.
