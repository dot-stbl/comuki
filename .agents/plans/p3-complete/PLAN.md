---
id: p3-complete
title: Finish Phase 3 — close the 7 design-system/testing gaps
status: in_progress
phase: 3
depends-on: [03-01, 03-02, 03-03]
parallelizable: false
---

# Plan: p3-complete — finish Phase 3

## Goal

Phase 3's scaffolding shipped (test stacks, tokens, 58 stories, 3 custom
components) but its **quality gates don't actually hold** and one promised
deliverable is missing. This plan closes all 7 gaps so Phase 3 is genuinely
"done" — not just "code on disk."

Diagnosis (audited 2026-07-06):
1. **Coverage gate neutered** — `dashboard/package.json` `test:coverage` ends
   in `|| true`; the 70% gate never fails CI.
2. **Visual-criticism cycle never run** — `StatusBadge`/`RunIdChip`/`ModeToggle`
   + the token swap were never screenshot-verified in 3 viewports against the
   rubric (`frontend-construct-rules.md` §4–5). Both 03-02/03-03 SUMMARYs say
   "manual visual review → deferred/skipped."
3. **a11y + story component-tests deferred on a wrong premise** — 03-03 claimed
   `addon-a11y`/`addon-vitest` are "v10-only". `addon-a11y` HAS an 8.x line
   (they installed `@latest`); `addon-vitest` is v10-only but the SB8-native
   substitute is `@storybook/test-runner`.
4. **DB integration test project missing** — CONTEXT 3.1 promised
   `Database.Runs.Integration.Migrations` (Testcontainers + Respawn are in deps
   but unused).
5. **No design-system drift detection** — DoD #4 wants a check that
   `.agents/docs/design-system/` tokens match `dashboard/src/index.css`.
6. **`build-storybook` not in CI** — DoD #3, only local.
7. **`test-backend` runs 2 of 8 test projects** — P4/P5 tests not in CI.

Tooling decisions (locked with user):
- a11y + story-tests: **SB8-native** — `@storybook/addon-a11y@^8` +
  `@storybook/test-runner@0.24`. No Storybook upgrade.
- Visual-regression baselines: **deferred to Phase 7** (no dashboard pages
  exist to baseline yet — legitimate, not a P3 gap).
- Visual cycle (gap 2): done with the **locally-installed `@playwright/test`**
  writing PNGs to disk + `getComputedStyle`, screenshots reviewed via the
  `read` tool (no browser MCP available; 0/0 servers).

## read_first
- .agents/STATE.md (sequencing note — P3 not finished)
- .agents/phases/03-design-system/CONTEXT.md (DoD)
- .agents/rules/coding/frontend-construct-rules.md §2–5 (stories, tests, visual cycle, DoD)
- .agents/rules/coding/TESTING-RULES.md (Testcontainers + Respawn pattern)
- .agents/rules/process/build-verification.md (dual-build)

## tasks

- [ ] **type: fix, gap 1 — un-neuter the coverage gate**
  - files: `dashboard/package.json`
  - change: `"test:coverage": "vitest run --coverage || true"` → `"test:coverage": "vitest run --coverage"`
  - verify: `cd dashboard && bun run test:coverage` exits non-zero when coverage < 70% (confirm `vitest.config.ts` threshold is 70)
  - accept: a low-coverage run fails; CI `test-frontend` now genuinely gates

- [ ] **type: feat, gap 3 — a11y + story component-tests (SB8-native)**
  - files: `dashboard/package.json`, `dashboard/.storybook/main.ts`, `dashboard/.storybook/preview.ts`
  - add deps: `@storybook/addon-a11y@^8`, `@storybook/test-runner@0.24` (+ `concurrently` if needed)
  - wire addon-a11y in `main.ts` (remove the 03-03 TODO); set a11y params in `preview.ts`
  - add script `"test:storybook": "test-storybook"`
  - verify: `bun run test:storybook` (storybook must be running) runs 58 stories; axe violations fail
  - accept: a11y addon active in Storybook; story-runner executes every story

- [ ] **type: feat, gap 6 — build-storybook in CI**
  - files: `.gitlab-ci.yml`
  - add `bun run build-storybook` to `test-frontend` (or a `build-storybook` job)
  - accept: CI builds the storybook static export on every FE change

- [ ] **type: fix, gap 7 — test-backend runs all test projects**
  - files: `.gitlab-ci.yml`
  - enumerate every `tests/**/*.csproj` via `dotnet run --no-build` (MTP); today only Architecture + Orchestration.Unit.Lease run
  - add: Routing.Unit.KeyRotation, Worker.Translator.Unit.StreamJson, Proxy.Integration.Rotation, Worker.Translator.Integration.TestTools.TestFakePi, Worker.Translator.Integration.PiCli (skip any that need services not in CI; document)
  - accept: every self-contained test project runs in CI

- [ ] **type: feat, gap 2 — visual-criticism cycle on 3 components + tokens**
  - boot `bun run storybook`; script `dashboard/scripts/visual-audit.mjs` uses `@playwright/test` to screenshot (375/768/1440) the `StatusBadge`/`RunIdChip`/`ModeToggle` stories + a token-palette story; dump `getComputedStyle` on suspect elements
  - review PNGs via `read`; walk the §4 rubric (spacing 4px-grid, rhythm, alignment, radii/shadow/type tokens, overflow, hierarchy, contrast, states, adaptive)
  - fix defects; re-shoot until clean
  - accept: rubric clean in all 3 viewports for all 3 components; defects fixed in-code

- [ ] **type: feat, gap 4 — Database.Runs integration test project**
  - files: `tests/Comuki.Platform.Database.Runs.Integration.Migrations/` (csproj + test), `comuki.slnx`
  - Testcontainers.PostgreSql + Respawn; test applies migrations, asserts schema, respawns
  - verify: `dotnet run --project …` green locally (needs Docker/podman running — check; if unavailable, ensure it builds + mark run as Docker-gated)
  - accept: integration test exists, builds, runs green where Docker is available; added to CI test-backend (docker-in-docker note)

- [ ] **type: feat, gap 5 — design-system drift detection**
  - files: `dashboard/scripts/check-design-tokens.mjs`, `dashboard/package.json`, `.gitlab-ci.yml`
  - parse token values from `.agents/docs/design-system/styles/tokens.css` (source of truth) and `dashboard/src/index.css`; diff; fail on mismatch
  - script `"check:design-tokens"`; wire into `test-frontend`
  - accept: a manual token edit in `index.css` that diverges from the design system fails the check

## verification
- `cd dashboard && bun run build && bun run test:coverage && bun run test:storybook` (all exit 0; coverage gate real)
- `dotnet build comuki.slnx -p:EnforceExtendedAnalyzerRules=true` → 0/0
- `dotnet format comuki.slnx --verify-no-changes --severity warn` → exit 0
- DB integration test runs green (Docker permitting)
- visual audit rubric clean for the 3 components in 3 viewports

## risks
- **gap 4 needs Docker** — Testcontainers requires a running daemon. Local
  podman VM may not be started; CI needs docker-in-docker. If unavailable,
  the project ships building-green with run gated behind Docker.
- **CI edits not locally verifiable** — gaps 6/7 wire `.gitlab-ci.yml`; no
  GitLab runner locally. Edits made carefully, validated on next pipeline.
- **gap 2 is the biggest item** — full §4 rubric across 3 viewports × 3
  components is real effort; may surface real visual defects to fix.
- **addon-a11y@8 + test-runner** — verify the 8.x line actually wires into
  SB 8.6 cleanly (the 03-03 attempt may have hit a real issue beyond versioning).
