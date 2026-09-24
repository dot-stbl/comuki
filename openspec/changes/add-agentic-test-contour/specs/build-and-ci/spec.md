## MODIFIED Requirements

### Requirement: CI jobs

CI SHALL run on pushes to master and pull requests (concurrency-cancelled per ref, read-only contents):

- `build backend` — checkout, .NET 10 setup, NuGet cache keyed on package pins + csproj hashes, `dotnet restore comuki.slnx`, `dotnet build comuki.slnx -c Debug --no-restore`
- `test backend` — the **full** `tests/unit/*` + `Comuki.Architecture.Tests` matrix, driven by the shared `scripts/ci/dotnet-test.mjs --tier=unit --full` script, each project run via `dotnet run --project <csproj> -c Debug` because xUnit v3 rides Microsoft Testing Platform and `dotnet test` cannot discover it. A hand-picked subset SHALL NOT be substituted for the full matrix in either CI system.
- `test backend (integration)` — the `tests/integration/*` suite, driven by `scripts/ci/dotnet-test.mjs --tier=integration`
- `test backend (agent-loop)` — the T2 agent-loop end-to-end suite (`agentic-testing` capability), driven by `scripts/ci/dotnet-test.mjs --tier=agent-loop --mode=fake`, requiring a Docker-capable runner
- `build frontend` — dashboard: bun install, `bun run typecheck`, `bun run lint`, `bun run test`; NON-BLOCKING (`continue-on-error`) while the dashboard lint config is mid-migration (known eslint plugin breakage; typecheck and vitest are green)

The GitHub Actions and GitLab pipelines SHALL invoke the same `scripts/ci/*.mjs` entry points for the backend test tiers, differing only in trigger conditions and runner shape, so the two pipelines cannot silently diverge on what "passing" means.

#### Scenario: MTP invocation
- **WHEN** CI runs a backend test project
- **THEN** it uses `dotnet run --project`, never `dotnet test`

#### Scenario: Frontend lint cannot block
- **WHEN** the dashboard lint step fails on the known config breakage
- **THEN** the workflow still succeeds; the step is explicitly non-blocking until the config is fixed

#### Scenario: Full matrix, not a subset
- **WHEN** either CI system runs the unit tier
- **THEN** every project under `tests/unit/*` plus `Comuki.Architecture.Tests` is included, not a fixed short list

#### Scenario: Identical logic across CIs
- **WHEN** the same commit is built on both GitHub Actions and GitLab
- **THEN** both invoke `scripts/ci/dotnet-test.mjs` with the same `--tier` arguments and report the same pass/fail set for that tier

## ADDED Requirements

### Requirement: Shared CI script layer
Backend test dispatch, tiered execution, and report generation SHALL live
in `scripts/ci/*.mjs` (bun/node, no Python), called identically from
`.github/workflows/ci.yml` and `deploy/hybrid/ci.yml`. Neither pipeline
SHALL hand-roll its own inline project list or glob loop where a shared
script exists for that purpose.

#### Scenario: One place to fix a tier's logic
- **WHEN** the unit-tier project discovery logic needs to change
- **THEN** the fix lands once in `scripts/ci/dotnet-test.mjs` and both CI pipelines pick it up on their next run

### Requirement: GitLab pipeline gates deploy on integration and agent-loop tiers
`deploy/hybrid/ci.yml` SHALL run a `test-e2e` stage (integration tier, then
agent-loop tier in fake mode) before the `build`/`deploy` stages, on a
Docker/Podman-capable runner. The `promote:dev` job SHALL depend on that
stage passing, in addition to the existing `test:unit` and
`migrate:validate` gates, once the new stage has been verified green on
the runner it targets.

#### Scenario: Broken agent-loop path blocks promotion
- **WHEN** the agent-loop tier fails on a pipeline targeting `master`
- **THEN** `promote:dev` does not run for that pipeline

#### Scenario: MR pipeline includes the new stage
- **WHEN** a merge request pipeline runs
- **THEN** `test-e2e` runs alongside the existing `test:unit` and `migrate:validate` jobs, not only on `master`

### Requirement: Manual live-eval and agent-QA gate carries a budget cap
`deploy/hybrid/ci.yml` SHALL expose a `qa` stage with `when: manual` jobs
for the hermetic compose suite (T3) and live-model evals/agent-QA (T4). The
live-eval job SHALL take an explicit `EVAL_BUDGET_USD` variable and SHALL
NOT default to unlimited spend. Neither `qa` job SHALL be a dependency of
`promote:dev`.

#### Scenario: No accidental spend
- **WHEN** a pipeline runs without an operator manually triggering the `qa:live-eval` job
- **THEN** no live-model call is made and no budget is consumed

#### Scenario: Budget variable required
- **WHEN** `qa:live-eval` is triggered without `EVAL_BUDGET_USD` set
- **THEN** the job fails fast with a message naming the missing variable, rather than running unbounded
