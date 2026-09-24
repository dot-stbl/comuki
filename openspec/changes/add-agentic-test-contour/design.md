## Context

See proposal.md for why. Grounded in what exists — extends assets, doesn't
replace them:

- `tests/tools/Comuki.TestFakePi` fakes the whole `pi` **process** (scripted
  stream-json transcript). It never exercises a real model call or the
  `agents/comuki-worker-sdk` hooks that run inside a real `pi` process.
- `tests/integration/Comuki.Host.Integration.Proxy/FakeUpstreamServer.cs`
  fakes an OpenAI-shape upstream HTTP endpoint in-process (one project only,
  not reusable, no Anthropic shape, no streaming, no cassette).
- `PiEnvironment.cs` already stamps `ANTHROPIC_BASE_URL` / `ANTHROPIC_AUTH_TOKEN`
  onto the pi `ProcessStartInfo` per work item (harden-pi-worker-sandbox,
  `bd61e47f`) — this is the exact mechanism a fake/replay model server reuses;
  no new stamping path is needed.
- `Comuki.Host.Translator.Integration.PiCli/TranslatorE2EShould` is the crown
  E2E test today: real Postgres, real in-proc worker host, real
  `TranslatorLoop`, but `PiRunner` execs `TestFakePi` as a bare subprocess —
  `Comuki.Engine.Compute` (Docker/K8s provisioning) is never in the loop.
- `Comuki.Engine.Orchestration.Unit.Eval` is a deterministic golden-file
  **status-machine replay tester** (`EvalRunner.Run`), not an agent/model
  quality eval — kept as-is, name collision avoided (see D8).
- Two CI systems exist and diverge: GitHub Actions runs the full integration
  suite but gates nothing that ships; GitLab's `deploy/hybrid/ci.yml` (the
  pipeline that builds and promotes images to `dev`) runs 3 hand-picked unit
  suites + a migration-idempotency check, no integration/E2E stage at all.
- Dashboard has Storybook 8 (93 stories) and Playwright as a devDependency,
  both essentially unused: `test:e2e` runs one spec (`landing.spec.ts`)
  against `bun run preview`; no interaction tests, no visual regression, no
  a11y checks are wired into any story.
- `harden-pi-worker-sandbox` (in flight) adds `SourceGitUrl`/`SourceGitRef` on
  `Project` and default-deny egress + minted virtual keys — this change reuses
  both: T2/T3 target repos clone through the same field, and live-mode
  scenarios mint a key through the same proxy path real runs use.

## Goals / Non-Goals

**Goals:**

- One scenario format, three execution modes (fake/replay/live), so a
  scenario authored once runs cheap-and-often (fake) and, unchanged, also
  validates against a recorded or live model.
- A fake-model HTTP server that both a real `pi` binary and the Brain's LLM
  client can be pointed at via `ANTHROPIC_BASE_URL`/`OPENAI_BASE_URL` — no
  code path knows it isn't talking to a real provider.
- CI-affordable tiers: T0/T1 fast and complete (not a hand-picked subset),
  T2/T3 real but bounded, T4 manual and budget-capped.
- One command (`test:affected`) and one report shape a coding agent can act
  on without a human translating CI output.
- GitLab and GitHub stop diverging: both call the same `scripts/ci/*.mjs`.

**Non-Goals (design-level):**

- A new orchestrator concept (Mission/WorkTask) — out of scope, see
  add-mission-cowork.
- Rewriting `agents/comuki-worker-sdk` pi-extensions (locks/skills/MCP
  enforcement) — harden-pi-worker-sandbox tasks 5.5/6.1 own that; this change
  consumes the extension points once they land and cross-references, not
  reimplements.
- A durable, cross-run eval-results database — T4 metrics land as CI
  artifacts + a flat history file first; a queryable store is a follow-up.
- Replacing k6 — this change wires the existing `tests/load/*.js` scripts
  into the T3 compose stack, it does not rewrite them.

## Architecture

```
                         ┌─────────────────────────────┐
                         │   scenario/<name>.yaml       │
                         │   ticket + cassette ref +    │
                         │   expected trajectory +      │
                         │   assertions                 │
                         └──────────────┬───────────────┘
                                        │ loaded by
                                        ▼
                         ┌─────────────────────────────┐
                         │   Scenario Runner            │
                         │   (Comuki.AgentTest.Runner)  │
                         │   mode: fake | replay | live │
                         └───┬─────────────┬─────────┬──┘
              seeds via REST │  points model│  drives │ asserts
                              │              │         │
         ┌────────────────┐  │  ┌───────────▼───────┐ │ ┌────────────────┐
         │ intake webhook  │◄─┘  │ Fake/Replay Model  │ │ │ journal /      │
         │ (real endpoint) │     │ Server             │ │ │ artifacts /    │
         └───────┬─────────┘     │ Anthropic + OpenAI │ │ │ sync-back      │
                  │               │ wire shapes,       │ │ │ (FakeGithub-   │
                  ▼               │ streaming,         │ │ │  SyncPort)     │
         ┌─────────────────┐      │ cassette record/   │ │ └───────▲────────┘
         │ run/work-item    │      │ replay             │ │         │
         │ creation + queue │      └─────────▲──────────┘ │         │
         │ claim (SKIP      │                │ ANTHROPIC_BASE_URL   │
         │ LOCKED)          │                │ (proxy virtual key,  │
         └───────┬──────────┘                │  same mint/stamp     │
                  │ real                      │  mechanism)          │
                  ▼                            │                     │
         ┌─────────────────┐                   │                     │
         │ Comuki.Engine.   │                   │                     │
         │ Compute (Docker) │───────────────────┘                     │
         │ real container   │                                         │
         └───────┬──────────┘                                         │
                  │ gRPC bidi stream                                   │
                  ▼                                                    │
         ┌─────────────────┐        T2b: real pi + worker-sdk hooks    │
         │ Translator loop  │────────────────────────────────────────►│
         │ + pi | TestFakePi│   T2a: TestFakePi only (container proof) │
         └─────────────────┘                                          │
                                                                         │
   T3 wraps ALL of the above in `deploy/compose.e2e.yml` and adds        │
   Playwright (dashboard) driving the real UI against the real REST/    │
   SignalR surface, plus k6 for load. ─────────────────────────────────┘
```

Tier boundaries map onto this diagram: T0 tests nothing above (pure unit);
T1 exercises `run/work-item creation + queue claim` and any single module
against a shared Postgres, no Compute/Translator; T2 is the whole diagram
minus the compose wrapper, against a single provisioned container; T3 is the
whole diagram inside the compose wrapper with Playwright/k6 attached; T4
reuses the Scenario Runner in `live` mode with a real model behind the hapy
gateway and adds the judge + corpus + metrics-history step.

## Scenario format

One format, three `mode` values. YAML on disk (JSON accepted — same schema),
loaded by both the C# runner (T1–T3) and the eval harness (T4).

```yaml
# tests/fixtures/scenarios/small-repo/add-null-check.scenario.yaml
schemaVersion: 1
name: add-null-check
description: >
  Ticket asks for a null guard on a public method in the fixture target
  repo. Difficulty: trivial. Exercises a single work item, no sub-tasks.
ticket:
  source: github                 # matches an intake webhook fixture shape
  title: "NullReferenceException in OrderTotal.Calculate"
  body: |
    Calculate() throws when orders is null. Add a guard and a test.
  labels: [bug]
  targetRepo:
    fixture: small-repo          # tests/fixtures/target-repos/small-repo
    ref: main
model:
  mode: fake                     # fake | replay | live
  cassette: cassettes/add-null-check.v1.json   # required for replay/live-record
  fakeScript: scripts/add-null-check.fake.json  # required for mode: fake
expectedTrajectory:
  - stage: plan
    minToolCalls: 1
    maxToolCalls: 3
  - stage: work-item
    toolsUsed: [Read, Edit, Bash]
    forbiddenTools: [WebFetch]     # asserts worker-sdk lock/allowlist held
assertions:
  journal:
    - condition: WorkspacePrepared
      expected: true
    - condition: AgentRunning
      expected: true
  run:
    finalStatus: succeeded
  diff:
    filesChanged:
      - path: "src/OrderTotal.cs"
        mustContain: "ArgumentNullException"
    testsAdded: true
  cost:
    maxUsdMicros: 50000            # only enforced in replay/live
  judge:                            # T4 only; ignored in fake/replay
    rubric: rubrics/brief-adherence.yaml
    minScore: 0.7
budget:
  maxUsd: 0.50                      # live mode only; runner aborts over this
```

`fake` scripts a fixed response sequence (deterministic, no cassette needed).
`replay` reads a recorded cassette and serves it byte-for-byte (fails loudly
on a request-shape mismatch instead of silently falling through to live).
`live` calls the fake-model server in **proxy** mode: it forwards to the real
model through the hapy gateway, optionally recording a new cassette as it
goes (this is how cassettes get (re-)recorded — `mode: live` +
`--record=cassettes/....json`).

## Fake-model design

New tool, `tests/tools/Comuki.TestFakeModel/` (C#, ASP.NET minimal API,
sibling to `Comuki.TestFakePi`, same audience — reusable in-process by xUnit
and packaged as a container image for compose).

- **Wire shapes:** `POST /v1/messages` (Anthropic Messages, incl.
  `stream: true` SSE with `message_start`/`content_block_delta`/
  `message_stop`) and `POST /v1/chat/completions` (OpenAI, incl. SSE
  `chat.completion.chunk`). Same shapes the real proxy already forwards
  (`Comuki.Modules.Proxy`) and the audit's `FakeUpstreamServer` already
  proves for the non-streaming OpenAI case.
- **Modes:**
  - `fake` — serves a `fakeScript` (ordered list of request→response pairs,
    matched by call index or by a cheap content predicate: last user message
    substring, or presence of a tool_result block). Fully deterministic,
    zero network, safe to run in CI on every PR.
  - `replay` — serves a recorded cassette; a request that doesn't match the
    next cassette entry's shape fails the scenario immediately (not a silent
    passthrough — a mismatched replay means the trajectory changed and the
    cassette is stale, which is signal, not noise).
  - `record` — forwards each request to the real model via the hapy gateway
    (using the scenario's minted virtual key) and appends the exchange to a
    new cassette file as it streams the response back, redacting secrets
    inline (see below) before the bytes ever touch disk.
- **Usable two ways:** `new FakeModelServer(...)` in-process inside an xUnit
  `IAsyncLifetime` fixture (T1/T2a/T2b, same pattern as
  `Comuki.Host.Testing.MinioImage`), and a published container image
  (`comuki-test-fake-model:latest`) for `deploy/compose.e2e.yml` (T3), so the
  same binary backs every tier.
- **Determinism knobs:** fixed message ids (`msg_<scenarioName>_<n>`), a
  fixed clock for any timestamp fields, and token-usage numbers taken from
  the fake script (so cost-ceiling assertions are testable without a real
  model in fake mode too).

## Cassette format and redaction

One JSON file per scenario per model, versioned (`schemaVersion`), array of
exchanges in call order:

```json
{
  "schemaVersion": 1,
  "scenario": "add-null-check",
  "recordedAt": "2026-09-23T00:00:00Z",
  "recordedAgainst": "claude-sonnet-5",
  "exchanges": [
    {
      "request": {
        "method": "POST",
        "path": "/v1/messages",
        "matchOn": { "lastUserMessageHash": "sha256:…", "toolResultPresent": false }
      },
      "response": {
        "status": 200,
        "streamed": true,
        "events": [
          { "type": "message_start", "data": { "id": "msg_add-null-check_1" } },
          { "type": "content_block_delta", "data": { "text": "I'll add a guard…" } },
          { "type": "message_stop", "data": { "usage": { "inputTokens": 412, "outputTokens": 96 } } }
        ]
      }
    }
  ]
}
```

- `request.matchOn` never stores the raw prompt — only a hash and a small set
  of structural predicates (tool names present, whether a tool_result block
  is present, a truncated/hashed last-message digest). This keeps cassettes
  diffable and small without keeping ticket/customer text verbatim in git.
- **Redaction** runs before a byte is written, not as a post-process: an
  allowlist of response fields is kept (text/tool_use/usage/stop_reason);
  every header is stripped except `content-type`; any string value matching
  a secret pattern (`sk-…`, `ck_…`, `Bearer …`, the minted virtual-key
  pattern, or a name on the redaction denylist — `ANTHROPIC_AUTH_TOKEN`,
  `COMUKI_WORKER_TOKEN`, any `Secrets:*` resolved value) is replaced with
  `"[redacted]"`. The recorder refuses to write a cassette entry it could not
  fully classify (fail closed, not "probably fine").
- Cassettes live under `tests/fixtures/scenarios/**/cassettes/` and are
  **committed** (small, redacted, deterministic) — replay mode never needs
  network or credentials, which is what makes T2/T3 hermetic.
- Re-recording: `bun run scripts/ci/record-cassette.mjs <scenario>` runs the
  scenario once in `mode: live` against a scoped eval project/budget and
  overwrites the cassette; a human reviews the diff like any other fixture
  change (no auto-commit).

## Tier table

| Tier | What | Where it runs | When | Cost / duration |
|---|---|---|---|---|
| T0 unit | `tests/unit/*` (full matrix) + `agents/*/src/**/*.test.ts` + dashboard vitest | GitHub Actions + GitLab `test:unit` (both, full matrix) | Every push/MR | Seconds–low minutes, no Docker |
| T1 integration | `tests/integration/*` on shared Postgres fixture + Respawn; new Knowledge/Compute/Settings suites | GitHub Actions (existing) + GitLab new `test:integration` stage (needs a Docker/Podman daemon runner) | Every push/MR | ~2–5 min after the shared-fixture fix (was ~10–50s/class × 19 classes) |
| T2 agent-loop E2E | Scenario Runner in `fake` mode: T2a (TestFakePi, real container, proves provisioning) + T2b (real `pi` + fake-model, proves worker-sdk hooks) | GitHub Actions (new job, Docker available); GitLab `test:e2e` stage before `promote:dev` once T1's speed fix lands | Every push/MR | ~1–3 min per scenario, zero model spend |
| T3 hermetic compose | `deploy/compose.e2e.yml`: postgres+pgvector, minio, host, worker (fake-model image), Playwright, k6 | GitHub Actions (new job); GitLab manual job (nova runner with a docker/podman daemon) | PR-triggered on GitHub; manual on GitLab (runner cost) | ~5–15 min, zero model spend |
| T4 live evals + agent-QA | Scenario Runner in `live`/`replay-refresh` mode + judge; agent-QA control-plane profile against a T3-shaped live target | Manual GitLab job + local `bun run eval:live` | Manual only (nightly schedule optional, never per-PR) | Real model spend, budget-capped per run |

## CI job layout — GitLab (`deploy/hybrid/ci.yml`)

```
stages:
  - test        # existing test:unit, now full matrix via shared script
  - test-e2e    # NEW: T1 (shared fixture) + T2 (fake mode), docker/podman runner
  - migrate     # existing migrate:validate, unchanged
  - build       # existing build:image / build:image:worker, unchanged
  - deploy      # existing secrets:dev / migrate:dev / promote:dev, unchanged
  - qa          # NEW: manual-only T3 + T4 jobs, budget-capped

test:unit:
  stage: test
  script: [bun run scripts/ci/dotnet-test.mjs --tier=unit --full]   # was a hand-picked 3-project list

test:integration:
  stage: test-e2e
  tags: [$NOVA_RUNNER_DOCKER_TAG]     # existing docker-capable runner tag
  script: [bun run scripts/ci/dotnet-test.mjs --tier=integration]

test:agent-loop:
  stage: test-e2e
  tags: [$NOVA_RUNNER_DOCKER_TAG]
  script: [bun run scripts/ci/dotnet-test.mjs --tier=agent-loop --mode=fake]

qa:hermetic-e2e:
  stage: qa
  tags: [$NOVA_RUNNER_DOCKER_TAG]
  when: manual
  script: [bun run scripts/ci/compose-e2e.mjs]

qa:live-eval:
  stage: qa
  when: manual
  variables: { EVAL_BUDGET_USD: "5.00" }
  script: [bun run scripts/ci/live-eval.mjs --budget=$EVAL_BUDGET_USD]
```

`promote:dev` gains a `needs:` on `test:integration` and `test:agent-loop` (in
addition to the existing `test:unit`/`migrate:validate`), so a broken
Translator/gRPC path can no longer ship to `dev` — closing the gap where
today only GitHub's non-gating pipeline notices. `qa:*` stays outside the
deploy critical path by design (manual, budget-capped, non-blocking).

GitHub Actions calls the **same** `scripts/ci/*.mjs` entry points from
`.github/workflows/ci.yml` (`test-be`/`test-integration` jobs invoke
`dotnet-test.mjs` with the same `--tier` flags), so the two pipelines run
identical logic and only differ in trigger/runner shape.

## Local dev commands

```bash
# One-time: point Testcontainers at Podman (Windows/WSL)
bun run scripts/test-env/podman-up.mjs      # docs the DOCKER_HOST/RYUK vars below, doesn't hide them
export DOCKER_HOST=npipe://./pipe/podman-machine-default
export TESTCONTAINERS_RYUK_DISABLED=true    # Ryuk's reaper container fights rootless Podman on Windows

# Feedback loop for a coding agent
bun run test:affected                        # git-diff-based dispatch across T0 (+T1 if touched)
bun run test:affected -- --tier=agent-loop    # force T2 fake-mode on a Translator/Compute change

# Fake model, standalone
dotnet run --project tests/tools/Comuki.TestFakeModel -- --mode=fake --script=... --port=17190

# Re-record a cassette against a real model (scoped budget/project)
bun run scripts/ci/record-cassette.mjs add-null-check --budget=1.00

# UI (dashboard) — single-run only, never a watch/dev server
bun run --cwd dashboard build-storybook       # static output, existing script
bun run --cwd dashboard test:storybook        # NEW: interaction + a11y + visual, one-shot against the static build
bun run --cwd dashboard ui-probe -- --story=runs-list--default --theme=dark   # NEW: screenshot+DOM/a11y+console, exits after one render
```

The UI probe starts a static file server bound to a scratch port, renders
exactly one story, captures artifacts, and shuts the server down before the
command exits — it is a bounded single-shot invocation, not the `bun run
dev`/watch/serve pattern AGENTS.md forbids from an agent session.

## Report format for agents

Every tier's runner writes the same envelope to `--out=<path>.json` and a
matching `<path>.md`, generated from the same object (no drift between
"what the JSON says" and "what the markdown says"):

```json
{
  "schemaVersion": 1,
  "tier": "agent-loop",
  "mode": "fake",
  "startedAt": "2026-09-23T12:00:00Z",
  "durationMs": 84213,
  "summary": { "total": 12, "passed": 11, "failed": 1, "skipped": 0 },
  "failures": [
    {
      "scenario": "add-null-check",
      "stage": "assertions.diff",
      "message": "src/OrderTotal.cs missing 'ArgumentNullException'",
      "artifactPaths": ["artifacts/add-null-check/run.log", "artifacts/add-null-check/diff.patch"]
    }
  ],
  "cost": { "usdMicros": 0, "tokensIn": 4120, "tokensOut": 960 }
}
```

The markdown report is the same data rendered as a table + a "first failure"
section with the artifact paths inlined — built so an agent reading
`test:affected`'s output can jump straight to the failing scenario's diff
without re-running anything. `test:affected` also prints a one-line verdict
to stdout (`PASS 11/12` / `FAIL 1/12 — see report.md`) for a human skimming a
terminal.

## Decisions

### D1. New capability `agentic-testing`, not folded into `build-and-ci`

The scenario format, fake-model server, and tiers are surface an agent (not
just CI) reads and drives directly — that is capability-shaped, not build
mechanics. `build-and-ci` gets the CI-wiring delta only (job layout, full
matrix, shared scripts, manual gate).

**Alternative:** one `testing` capability covering both. Rejected — the
existing `build-and-ci` capability already owns "the build gate is a
contract"; splitting keeps that contract's diff small and reviewable.

### D2. Fake-model is a new tool, not a `TestFakePi` mode

`TestFakePi` fakes the **agent process**; the fake-model fakes the
**upstream LLM HTTP surface**. They compose (T2b runs the real `pi` binary
against the fake-model) but are not the same seam — folding them would make
`TestFakePi` simultaneously implement two wire protocols (stream-json out,
Anthropic/OpenAI HTTP in) for no shared benefit.

### D3. T2 splits into T2a (container proof) and T2b (worker-sdk proof)

T2a (TestFakePi in a real container) is cheap and proves
Compute-provisioning → gRPC → journal without touching a model at all — it
is the natural next step after `TranslatorE2EShould`. T2b (real `pi` +
fake-model) is the only place that exercises `agents/comuki-worker-sdk`
hooks in situ, closing the gap harden-pi-worker-sandbox task 6.1 names.
Keeping them separate lets T2a stay fast even before pi-extensions land.

### D4. Target repos are fixtures cloned through `SourceGitUrl`, not synthesized in-process

Reuses the harden-pi-worker-sandbox clone path instead of inventing a second
workspace-prep mechanism. `tests/fixtures/target-repos/*` are small, real git
repos (checked in as repos-within-the-repo or a local bare remote spun up by
the fixture) at varied difficulty; a handful of anonymized real tickets are
scenario-only (ticket text), pointed at the same small fixture repos, not at
customer repos.

### D5. Live/replay budget enforcement is defense-in-depth, not solely the Costs gate

`budget.maxUsd` in the scenario is enforced by the Scenario Runner itself
(abort mid-run over budget) in addition to whatever the target project's
existing hard-stop budget gate does — a test run against a shared eval
project should not depend on remembering to configure that project's budget
correctly every time.

### D6. T4 harness name: `Comuki.AgentEval`, distinct from `Comuki.Engine.Orchestration.Unit.Eval`

The existing project stays exactly as-is (status-machine golden replay). The
new harness lives at `tests/tools/Comuki.AgentEval/` — a runner + judge +
corpus, not a `tests/unit/*` project (it needs a real or replayed model and
is never part of the T0 matrix).

## Risks / Trade-offs

- **[Risk] Cassettes go stale silently** → Mitigation: replay mode fails
  loudly on a request-shape mismatch (D-format above) rather than degrading
  to live; `record-cassette.mjs` is a named, auditable command, not a CI
  side-effect.
- **[Risk] T2/T3 in GitLab need a docker/podman-capable runner that may not
  exist yet on the `nova-dev-docker` tag** → Mitigation: T1's shared-fixture
  fix (harden-pi-worker-sandbox precedent: fix cost before adding scope)
  lands first in the same workstream ordering; `test-e2e` stage is added
  behind the existing runner tag, verified before `promote:dev` gets a
  `needs:` on it.
- **[Risk] Redaction misses a secret shape** → Mitigation: fail-closed
  classifier (unclassified field → cassette write refused, not "written
  anyway"); redaction rules live in one reviewable file, not scattered.
- **[Risk] Storybook interaction/visual tests are flaky on font/rendering
  differences across CI vs local** → Mitigation: visual baselines are
  generated and reviewed in CI (not committed from a dev machine); a
  documented tolerance threshold, not pixel-exact.
- **[Risk] Agent-QA worker filing issues on Comuki's own repo could be
  noisy** → Mitigation: manual-trigger only, profile posts to a labeled
  `agent-qa` bucket for human triage, same review gate as `pr-review`'s
  verdict comment.
- **[Risk] Scope is large for one change** → Mitigation: tasks.md splits it
  into independently-deliverable, disjoint-file workstreams so it ships
  incrementally, T1 first (load-bearing prerequisite), T4/UI-probe last.

## Migration Plan

Additive throughout — no existing test, spec, or CI job is removed. The
GitLab `test:unit` full-matrix change and the new `test-e2e`/`qa` stages are
the only behavior change to an existing pipeline; both are staged so
`promote:dev` only gains new `needs:` once the referenced jobs are green on
a few pipelines first (tracked in tasks.md, not silently flipped). Restoring
`testing-unit.md`/`testing-integration.md` under `.agents/rules/coding/`
adapts (not copies) the user-global `~/.agents/rules/csharp/testing-*.md`
templates to Comuki's actual stack (xUnit v3/MTP not v2, `HostComposer.Compose`
not `WebApplicationFactory<Program>`, `Comuki.Host.Testing` as the existing
shared-fixture seed) — those user-global files stay as generic scaffolding,
not overwritten.

## Open Questions

- Exact runner/tag for GitLab's docker/podman-capable lane (`$NOVA_RUNNER_DOCKER_TAG`
  is assumed to exist per the nova shared-modules convention; confirm with
  the platform-runner owner before wiring `test-e2e`/`qa` stages — if it
  doesn't exist yet, provisioning it is a prerequisite task, not a spec hole).
- Where T4's metrics-over-time history lives long-term (flat JSON history
  file in the repo vs. a small dashboard-visible store) — flat file for v1,
  revisit once agent-quality trends are actually being read by someone.
- Whether the anonymized-real-ticket subset needs a formal anonymization
  review step before landing as fixtures — flagged for whoever authors the
  corpus workstream, not resolved here.
