## Why

Comuki's core promise — ticket → brain → plan → workers in containers →
result → sync-back — has no automated test for the *chain*.
`TranslatorE2EShould` proves claim→stream→journal→complete for one work item
with no Brain, container, or model involved. `Comuki.Engine.Orchestration.Unit.Eval`
is a deterministic status-machine replay, not an agent-quality eval — zero
rubric-scored coverage of "did the brain decompose this well" exists. CI is
split and both halves lie: GitHub Actions runs the integration suite but
gates nothing real; GitLab's `deploy/hybrid/ci.yml` — the pipeline that
actually promotes images to `dev` — runs 3 hand-picked unit suites and skips
integration entirely. Only 2 of ~38 unit projects run explicitly in either
CI. Dashboard has Storybook (93 stories) and Playwright wired but unused:
one E2E spec, no interaction/visual/a11y checks. Coding agents here have no
single feedback command and no green-gate-before-MR rule.

## What Changes

- One declarative **scenario format** (ticket fixture + model cassette +
  expected trajectory + assertions) executed in three modes — **fake**
  (scripted fake model), **replay** (recorded real-model cassette,
  re-recordable), **live** (real model via the hapy gateway).
- A C# **fake model** (`tests/tools/`, alongside `Comuki.TestFakePi`):
  Anthropic Messages + OpenAI chat-completions wire shapes, streaming,
  scripted responses, cassette record/replay with secret redaction; usable
  in-process in xUnit and as a container image, pointed at via
  `ANTHROPIC_BASE_URL` (the same mechanism the proxy already uses to stamp
  virtual keys).
- Five tiers: **T0** unit (full matrix, not 2–3 projects), **T1** integration
  foundation (shared Postgres fixture + Respawn + reuse, shared test-infra
  lib, missing Knowledge/Compute/Settings suites, restored testing-rule
  docs), **T2** agent-loop E2E (real container, real `pi` or `TestFakePi`,
  fake model), **T3** hermetic compose e2e (Playwright + k6, no live key),
  **T4** live evals + agent-QA (manual only, budget-capped).
- A control-plane **agent-QA profile** exercising Comuki's own stack via
  CLI/API/browser, filing issues — same pattern as `pr-review`.
- **Storybook** interaction tests, visual snapshots (light/dark), axe a11y
  per story, plus an agent-facing **UI probe** (single-run render →
  screenshot + DOM/a11y tree + console log, no dev server).
- A `test:affected` feedback command with JSON+markdown reports, shared
  `scripts/ci/*.mjs` for both GitHub Actions and GitLab, and a GitLab test
  stage before `promote:dev`.

## Capabilities

### New Capabilities

- `agentic-testing`: scenario format, fake-model harness, cassette
  record/replay, T1–T4 tiers, agent-QA profile, Storybook interaction/visual/
  a11y coverage, UI probe, `test:affected` feedback loop, local Podman
  test-env tooling.

### Modified Capabilities

- `build-and-ci`: full unit matrix in CI, shared bun/node CI scripts, GitLab
  test stage before `promote:dev`, manual live-eval/agent-QA gate with a
  budget cap.

## Impact

`tests/tools/`, `tests/integration/`, `dashboard/.storybook`,
`dashboard/e2e/`, `.github/workflows/ci.yml`, `deploy/hybrid/ci.yml`,
`scripts/ci/`, `scripts/test-env/`, `.agents/rules/coding/testing-*.md`,
`control-plane/profiles/`. No production runtime path changes.

## Non-goals

- Replacing `Comuki.Engine.Orchestration.Unit.Eval` — stays the
  status-machine golden tester; the new T4 harness is named distinctly.
- Owning pi-extension lock/skill/MCP enforcement (harden-pi-worker-sandbox
  5.5/6.1) or memory/template eval suites (add-mission-cowork 7.8/15.3/18.3)
  — this change consumes and cross-references both, not duplicates them.
- Running live evals or agent-QA on every PR — manual-trigger only, cost
  and non-determinism don't belong in the per-PR gate.
- A new orchestrator concept (no Mission/WorkTask coupling) — test-and-CI
  infrastructure only.
