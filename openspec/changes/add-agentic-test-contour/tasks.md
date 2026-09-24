# Tasks — add-agentic-test-contour

Grouped into independently-deliverable workstreams, each sized for one
agent/MR with disjoint file areas. `Depends on` lists hard prerequisites
(the dependency's acceptance criteria must be met, not just started).
Workstreams with no listed dependency can start immediately, in parallel.

Cross-references — do not duplicate, coordinate instead:
- `openspec/changes/harden-pi-worker-sandbox` tasks 5.5 (`translator --fixture`)
  and 6.1–6.3 (pi-extensions: locks/skills/MCP) are owned by that change.
  WS7 below consumes 6.1's extension point once it lands; it does not
  reimplement lock/skill/MCP enforcement.
- `openspec/changes/add-mission-cowork` tasks 7.8/15.3/18.3 are
  memory/template eval suites (retrieval, compaction, security, template
  DAG validation) — a different eval surface. WS10 below cross-links them,
  it does not fold them in.

---

## WS0 — Restore Comuki-specific testing rules

**Depends on:** none. **Size:** S.
**Files:** `.agents/rules/coding/testing-unit.md` (new), `.agents/rules/coding/testing-integration.md` (new).

- [ ] 0.1 Author `.agents/rules/coding/testing-unit.md` for Comuki's actual
      stack (xUnit v3/MTP via `dotnet run --project`, Shouldly, NSubstitute,
      `<Subject>Should` + Given/When/Then `DisplayName`) — adapt, do not
      copy, `~/.agents/rules/csharp/testing-unit.md` (user-global template;
      leave it untouched).
- [ ] 0.2 Author `.agents/rules/coding/testing-integration.md` documenting
      Comuki's real pattern: `HostComposer.Compose` (not
      `WebApplicationFactory<Program>` — `Program.cs` is top-level, see
      AGENTS.md), `Comuki.Host.Testing` as the existing shared fixture seed,
      the target end-state this change builds toward (`ICollectionFixture`
      + Respawn + `WithReuse`, see WS2).
      Frontmatter matches `.agents/rules/coding/FRAMEWORK-RULES.md`
      (`description`, `always: true`, `globs`).

**Acceptance:** both files exist, frontmatter valid, cross-link each other
and `testing-stack-and-pyramid.md` conventions; no change to any `.cs` file.

---

## WS1 — CI completeness & shared script foundation (T0)

**Depends on:** none. **Size:** M.
**Files:** `scripts/ci/*.mjs` (new), `.github/workflows/ci.yml`, `deploy/hybrid/ci.yml`.

- [ ] 1.1 `scripts/ci/dotnet-test.mjs --tier=unit [--full]`: globs
      `tests/unit/*/` the same way `test-integration` already globs
      `tests/integration/*/`, runs each via `dotnet run --project <csproj>`,
      emits the JSON+markdown report shape from design.md.
- [ ] 1.2 Point GitHub Actions `test-be` at the script, full matrix
      (replacing the 2-project hard-coded matrix).
- [ ] 1.3 Point GitLab `test:unit` at the same script, full matrix
      (replacing the 3-project hard-coded list); keep the job comment
      explaining the change from "fast subset" to "full, script-driven."
- [ ] 1.4 `scripts/ci/dotnet-test.mjs --tier=integration` wraps the existing
      integration-suite loop (both CIs call it) — no behavior change yet,
      just de-duplicating the two hand-rolled glob loops into one script.

**Acceptance:** `dotnet-test.mjs --tier=unit --full` run locally covers all
`tests/unit/*` + `Comuki.Architecture.Tests` projects; both CI YAMLs
reference the script, not inline project lists; GitHub and GitLab produce
the same pass/fail set on the same commit.

---

## WS2 — T1 foundation: shared Postgres fixture + Respawn + reuse

**Depends on:** WS0 (rules land first so the fixture follows them).
**Size:** L.
**Files:** `tests/integration/Comuki.Host.Testing/*` (extend), new
`tests/integration/Comuki.Host.Testing/Fixtures/PostgresCollectionFixture.cs`,
conversions in `Comuki.Host.Integration.{Chat,Costs,Errors,Proxy,Realtime,Runs,Secrets,Workers}`.

- [ ] 2.1 Add a `PostgresCollectionFixture` (`ICollectionFixture` +
      `[CollectionDefinition]`, `WithReuse(true)`) to `Comuki.Host.Testing`
      that migrates all 9 module `DbContext`s once per test-run process,
      alongside a `Respawn`-based `ResetAsync()`.
- [ ] 2.2 Convert the two existing `[CollectionDefinition]` consumers
      (`Comuki.Host.Integration.Auth`, `Comuki.Host.Integration.Intake`) to
      the shared fixture — proves the pattern before the wider rollout.
- [ ] 2.3 Convert the highest-cost remaining projects (per the audit: Chat,
      Costs, Errors, Proxy, Realtime, Runs, Secrets, Workers) from
      per-class `PostgreSqlBuilder` + `MigrateAsync` to the shared fixture +
      `ResetAsync()` between tests.
      Remaining integration projects not listed here get a follow-up issue,
      referenced from tasks.md, not silently dropped.
- [ ] 2.4 Consolidate duplicated test-infra into `Comuki.Host.Testing`:
      `FakeTimeProvider` (4 copies → 1), `FixedClock` (3 → 1),
      `TempControlPlaneRoot` (2 → 1). `WaitForAsync`, `FakeGithubSyncPort`,
      `FakeUpstreamServer` already single-copy — leave in place, document
      as shared in WS0's rule doc.
- [ ] 2.5 `dotnet build comuki.slnx -c Debug` and every converted project's
      suite pass unchanged (same test count, no behavior drift).

**Acceptance:** the two `AuthIntegrationCollection`/`IntakeHostCollection`
consumers plus the eight listed projects run against one shared, reused
Postgres container with `Respawn` reset between tests; measured wall-clock
for those ten projects together drops materially vs. per-class containers
(record before/after in the PR description, no fixed target number here).

---

## WS3 — T1 missing suites: Knowledge / Compute / Settings

**Depends on:** WS2. **Size:** M.
**Files:** new `tests/integration/Comuki.Modules.Knowledge.Integration/`,
new `tests/integration/Comuki.Host.Integration.Compute/`, new
`tests/integration/Comuki.Host.Integration.Settings/`.

- [ ] 3.1 `Comuki.Modules.Knowledge.Integration`: `PgKnowledgeSearcherShould`
      against `pgvector/pgvector:pg16` via the WS2 shared fixture — the
      `search_knowledge` MCP tool path currently has zero integration
      coverage (audit §1.5/§4.1.5).
- [ ] 3.2 `Comuki.Host.Integration.Compute`: HTTP-route-level tests for
      `ComputeSnapshotEndpoints` (currently no dedicated integration
      project or unit suite, per the 2026-09-23 backend audit).
- [ ] 3.3 `Comuki.Host.Integration.Settings`: HTTP-route-level tests for
      `SettingsEndpoints` (same gap).
      (`Mcp` unit coverage — `Comuki.Host.Unit.Mcp`, 34 tests — already
      exists; do not re-task it. If Mcp HTTP-route integration coverage is
      still missing when this workstream starts, fold a
      `Comuki.Host.Integration.Mcp` suite in here; verify first.)

**Acceptance:** all three new projects build and pass against the shared
fixture; each closes a named, verified gap (re-check the gap still exists
before writing the suite — audits age).

---

## WS4 — Fake-model core (Anthropic wire shape, in-process)

**Depends on:** none. **Size:** M.
**Files:** new `tests/tools/Comuki.TestFakeModel/` (Program.cs, Anthropic
handler, in-process host builder).

- [ ] 4.1 `POST /v1/messages` handler: non-streaming + `stream: true` SSE
      (`message_start`/`content_block_delta`/`message_stop`), scripted via
      an ordered `fakeScript` (request-index or content-predicate matched).
- [ ] 4.2 In-process usage: a fixture class usable from an xUnit
      `IAsyncLifetime`, same shape as `Comuki.Host.Testing.MinioImage`.
- [ ] 4.3 Deterministic ids/usage numbers per design.md's "Determinism
      knobs."
- [ ] 4.4 Unit tests for the fake server itself (script matching, SSE
      framing, id determinism).

**Acceptance:** a real `pi` binary pointed at this server via
`ANTHROPIC_BASE_URL` completes a scripted single-turn exchange end to end
(manual smoke, recorded in the PR); in-process fixture usable from a
throwaway xUnit test.

---

## WS5 — Fake-model OpenAI shape + cassette format + container image

**Depends on:** WS4. **Size:** M.
**Files:** `tests/tools/Comuki.TestFakeModel/` (extend), new
`tests/tools/Comuki.TestFakeModel/Cassettes/` (redaction, reader/writer),
new `deploy/testfakemodel.Dockerfile`.

- [ ] 5.1 `POST /v1/chat/completions` handler, non-streaming + SSE chunks,
      same script/matching mechanism as WS4.
- [ ] 5.2 Cassette reader/writer per design.md's format; `record` mode
      forwards to a configured upstream and writes redacted exchanges.
- [ ] 5.3 Redaction classifier: allowlisted response fields, header
      stripping, secret-pattern scrub, fail-closed on an unclassified
      field (refuse to write, not "write anyway").
- [ ] 5.4 `replay` mode: serves a cassette, fails loudly (typed error, not
      a silent passthrough) on a request-shape mismatch against the next
      expected entry.
- [ ] 5.5 Container image (`deploy/testfakemodel.Dockerfile`), sanity-run
      like the worker image (`--mode=fake` entrypoint override, one scripted
      exchange).

**Acceptance:** a cassette recorded against WS4's in-process server round-
trips through `record` → `replay` with byte-identical served responses
(modulo redacted fields); redaction unit tests cover header/body/query
secret shapes; image builds and sanity-runs.

---

## WS6 — Scenario format + runner + T2a (container-lifecycle proof)

**Depends on:** WS2. **Size:** L.
**Files:** new `tests/tools/Comuki.AgentTest.Runner/`, new
`tests/fixtures/scenarios/` (schema + a first scenario + a small target
repo fixture), new `tests/integration/Comuki.EndToEnd.AgentLoop/`.

- [ ] 6.1 Scenario schema (design.md's YAML shape) + loader + JSON-schema
      validation; `tests/fixtures/scenarios/small-repo/add-null-check.scenario.yaml`
      as the first authored scenario.
- [ ] 6.2 `tests/fixtures/target-repos/small-repo`: a tiny fixture repo
      (few files, one real bug) the scenario clones via the
      `SourceGitUrl`/`SourceGitRef` mechanism (harden-pi-worker-sandbox).
- [ ] 6.3 `Comuki.EndToEnd.AgentLoop`: seeds an intake webhook payload →
      real webhook endpoint → run/work-item creation → real queue claim →
      **real Docker container** via `Comuki.Engine.Compute.Docker` running
      the worker image with `TestFakePi` swapped in for `pi` → gRPC stream
      → journal → artifact. No model involved (T2a).
- [ ] 6.4 Scenario Runner asserts journal conditions + final run status
      from the scenario file against the T2a run; report emitted in the
      design.md JSON+markdown shape.

**Acceptance:** `add-null-check` runs T2a end to end against a real
provisioned container and passes; a deliberately-broken fixture (bad image
label) fails with a report that names the failing assertion, not a stack
trace dump.

---

## WS7 — T2b: real pi + fake-model (worker-sdk hook proof)

**Depends on:** WS4, WS6. **Size:** M.
**Files:** `tests/integration/Comuki.EndToEnd.AgentLoop/` (extend, same
project as WS6 — sequential, not parallel with it).

- [ ] 7.1 Extend the scenario runner to point the real `pi` binary at
      WS4's fake-model server via `ANTHROPIC_BASE_URL` (same stamp
      mechanism as the minted virtual key), inside the same provisioned
      container as T2a.
- [ ] 7.2 Assert `expectedTrajectory.forbiddenTools` against the actual
      tool calls pi made — this is the first test anywhere that observes
      `agents/comuki-worker-sdk` lock/skill/MCP enforcement in situ.
      **Coordinate with `harden-pi-worker-sandbox` 6.1–6.3**: if those
      pi-extensions haven't landed yet, this assertion is written against
      the extension's documented contract and marked
      `[Fact(Skip = "blocked on harden-pi-worker-sandbox 6.1, gh-issue #125")]`
      until it does — do not reimplement lock enforcement here.
- [ ] 7.3 Diff/artifact assertions (`assertions.diff` in the scenario)
      against the real edit pi made in the fixture repo.

**Acceptance:** with harden-pi-worker-sandbox 6.1 landed, `add-null-check`
passes T2b with the forbidden-tool assertion actually enforced; without it,
the suite is green with the one documented skip, not silently omitted.

---

## WS8 — Replay mode + re-record command

**Depends on:** WS5, WS6. **Size:** S.
**Files:** `scripts/ci/record-cassette.mjs` (new), scenario runner mode
wiring (extends WS6/7's runner, no new project).

- [ ] 8.1 `bun run scripts/ci/record-cassette.mjs <scenario> --budget=<usd>`:
      runs the named scenario in `live` record mode against a scoped
      eval project, writes/overwrites the cassette.
- [ ] 8.2 Wire `mode: replay` in the scenario runner: serves the committed
      cassette, no network.
- [ ] 8.3 One committed replay-mode scenario as a worked example.

**Acceptance:** the worked example passes in `replay` mode with
`TESTCONTAINERS_RYUK_DISABLED` set and no outbound network (verified by
running it with network access blocked).

---

## WS9 — Live mode wiring + budget cap

**Depends on:** WS6, WS5. **Size:** S.
**Files:** scenario runner (extend), `scripts/ci/live-eval.mjs` (new).

- [ ] 9.1 `mode: live`: fake-model server proxies to the hapy gateway using
      a minted virtual key scoped to the scenario's eval project.
- [ ] 9.2 Runner-side `budget.maxUsd` enforcement (abort mid-run over
      budget), independent of the target project's own budget gate.
- [ ] 9.3 `scripts/ci/live-eval.mjs --budget=<usd>`: local/manual entry
      point, prints the same JSON+markdown report.

**Acceptance:** a live run against a real model (small scoped budget)
completes and the report shows real `cost.usdMicros`; a run given an
artificially tiny budget aborts before exceeding it.

---

## WS10 — T4 golden-task eval harness (`Comuki.AgentEval`)

**Depends on:** WS8, WS9. **Size:** L.
**Files:** new `tests/tools/Comuki.AgentEval/` (runner, judge, corpus
loader), new `tests/fixtures/scenarios/` corpus entries (synthetic +
anonymized-real subset), new `.agents/docs/audits/` or `tests/fixtures/scenarios/README.md`
history file location (flat JSON per design.md's open question).

- [ ] 10.1 Corpus: extend `tests/fixtures/scenarios/` with varied-difficulty
      synthetic tickets against the WS6 fixture repo(s), plus a small
      anonymized-real-ticket subset (flag anonymization review as a PR
      checklist item, per design.md's open question).
- [ ] 10.2 LLM-as-judge + rubric scoring (`assertions.judge` in the
      scenario schema) — a rubric file format, a judge call through the
      hapy gateway, score persisted in the report.
- [ ] 10.3 Quality+cost metrics tracked over time: append each run's
      summary to a flat history file; a small `bun run eval:trend` reader.
- [ ] 10.4 Explicitly cross-link, do not duplicate:
      `openspec/specs/../add-mission-cowork` tasks 7.8/15.3/18.3 (memory/
      template evals) in this project's README — different corpus,
      different judge target.

**Acceptance:** `Comuki.AgentEval` and `Comuki.Engine.Orchestration.Unit.Eval`
coexist with no naming or behavior collision (grep confirms no cross-
references); a full corpus run against `replay` mode completes and produces
a trend-appendable report.

---

## WS11 — Local dev container-runtime tooling

**Depends on:** none. **Size:** S.
**Files:** new `scripts/test-env/podman-up.mjs`, new
`.agents/rules/process/local-test-runtime.md`.

- [ ] 11.1 `scripts/test-env/podman-up.mjs`: checks/starts a Podman machine
      on Windows/WSL, prints the `DOCKER_HOST=npipe://./pipe/podman-machine-default`
      and `TESTCONTAINERS_RYUK_DISABLED=true` exports (documents, doesn't
      silently `export` into the caller's shell — Windows/PowerShell can't
      inherit env back to the parent process).
- [ ] 11.2 Rule doc covering: Podman vs Docker locally, the two env vars
      and why, and the GitLab `$NOVA_RUNNER_DOCKER_TAG` equivalent in CI.

**Acceptance:** following the script's printed instructions, a fresh
Windows dev machine can run WS2's shared-fixture integration suite against
Podman with no manual Testcontainers troubleshooting.

---

## WS12 — GitLab CI wiring: test-e2e + qa stages

**Depends on:** WS1, WS2, WS3, WS6, WS7. **Size:** M.
**Files:** `deploy/hybrid/ci.yml` (extend — coordinate with anyone else
touching this file; harden-pi-worker-sandbox's remaining tasks do not
currently touch it, verified against its tasks.md).

- [ ] 12.1 Add `test-e2e` stage (`test:integration`, `test:agent-loop`)
      per design.md's CI job layout, on the docker/podman-capable runner
      tag — confirm the tag exists first (design.md's open question); if
      it doesn't, provisioning it is a blocking sub-task here, not a spec
      hole.
- [ ] 12.2 Add `qa` stage (`qa:hermetic-e2e`, `qa:live-eval`), both
      `when: manual`, `qa:live-eval` carrying `EVAL_BUDGET_USD`.
- [ ] 12.3 Add `needs:` from the deploy stage's promote job onto
      `test:integration` and `test:agent-loop` — staged: land the jobs
      green on a few pipelines first, add the `needs:` gate in a follow-up
      commit within this same workstream, not silently on day one.

**Acceptance:** a pipeline run on this branch shows `test-e2e` passing
before `build`/`deploy`; `qa:*` jobs are visibly manual-only in the
pipeline graph; `promote:dev` is blocked by a red `test:integration`.

---

## WS13 — T3 hermetic compose stack

**Depends on:** WS5, WS7. **Size:** L.
**Files:** new `deploy/compose.e2e.yml`.

- [ ] 13.1 `deploy/compose.e2e.yml`: postgres+pgvector, minio, host image,
      worker image with the WS5 fake-model image swapped in for `pi`'s
      upstream, no live Anthropic key required.
- [ ] 13.2 Boot/seed/teardown script (`scripts/ci/compose-e2e.mjs`): starts
      the stack, posts a webhook, polls run status via REST, asserts an
      artifact lands in MinIO and a SignalR event fires — the full
      ticket→brain→plan→worker→result→sync-back chain, zero model spend.

**Acceptance:** `bun run scripts/ci/compose-e2e.mjs` boots, runs one
scenario end to end, tears down cleanly (no orphaned containers), exits
non-zero on assertion failure.

---

## WS14 — Playwright + k6 against the T3 stack

**Depends on:** WS13. **Size:** M.
**Files:** `dashboard/e2e/*` (extend beyond `landing.spec.ts`),
`dashboard/playwright.config.ts` (extend, new e2e project config), `tests/load/*.js` (point at compose target, no rewrite).

- [ ] 14.1 `dashboard/e2e/runs-flow.spec.ts`: login → runs list → approve/
      cancel → realtime update, driven against WS13's compose stack (not
      `VITE_USE_MOCK`) — seed data adapted from `dashboard/src/shared/api/mock/*`.
- [ ] 14.2 Wire `tests/load/*.js` (k6) at the compose target per
      `tests/load/README.md`'s existing proposed shape — promote from
      smoke to functional assertions where the README already sketches it.

**Acceptance:** the new Playwright spec passes against the compose stack in
CI; k6 scripts run against the same stack and their existing SLO thresholds
still apply.

---

## WS15 — Agent-QA control-plane profile

**Depends on:** WS13 (needs a target to exercise for a real run; profile
authoring itself can start earlier). **Size:** M.
**Files:** new `control-plane/profiles/self-qa.md`, new
`control-plane/skills/self-qa/SKILL.md`.

- [ ] 15.1 Author `control-plane/profiles/self-qa.md` modeled on
      `control-plane/profiles/pr-review.md`: a worker briefed to exercise
      Comuki's own CLI/API/dashboard (against the WS13 compose stack or a
      dev deployment) and report findings — read-mostly, files GitHub
      issues labeled `agent-qa` rather than editing product code.
- [ ] 15.2 `control-plane/skills/self-qa/SKILL.md`: sequences what to probe
      (a checklist derived from the tier table's "what's covered" gaps —
      it should look for the things T0–T3 don't catch: cross-domain UX
      breakage, confusing error copy, slow flows) and the issue-filing
      format.
- [ ] 15.3 Manual-only invocation documented (local command + the WS12
      `qa` stage does not auto-run this profile without an operator
      trigger).

**Acceptance:** a manual run of the profile against a running Comuki
instance produces at least one labeled, evidence-backed GitHub issue
(or explicitly "no findings" with the checklist marked complete) — same
verdict discipline as `pr-review`.

---

## WS16 — Storybook UI testing: interaction + visual + a11y

**Depends on:** none. **Size:** L.
**Files:** `dashboard/.storybook/*` (extend), `dashboard/package.json`
(new `test:storybook` script), story files gain `play` functions
incrementally (start with the highest-traffic domains: runs, chat,
identity — not all 93 stories in one workstream).

- [ ] 16.1 Wire `@storybook/test` interaction testing + `play` functions on
      a first batch of stories (runs, chat domains); `test:storybook`
      script runs them one-shot against `build-storybook`'s static output
      (no watch/dev server).
- [ ] 16.2 Visual snapshot testing per story, light/dark themes, against a
      committed baseline; CI-generated baselines (not dev-machine
      screenshots), documented tolerance threshold.
- [ ] 16.3 axe a11y check per story wired into the same one-shot run;
      failures report the violated rule + story id, not just "failed."
- [ ] 16.4 Remaining stories get a tracked follow-up (not silently
      dropped) — this workstream ships the harness + first batch, not
      necessarily all 93 at once.

**Acceptance:** `bun run --cwd dashboard test:storybook` runs interaction +
visual + a11y for the first batch in one shot, produces a report in the
design.md JSON+markdown shape, and is wired into `.github/workflows/ci.yml`'s
`build-fe` job (or a new sibling job).

---

## WS17 — Agent-facing UI probe command

**Depends on:** WS16. **Size:** M.
**Files:** new `dashboard/scripts/ui-probe.ts`, `dashboard/package.json`
(new `ui-probe` script).

- [ ] 17.1 `bun run --cwd dashboard ui-probe -- --story=<id> --theme=<light|dark>`:
      starts a static server bound to a scratch port (17180–17200 pool),
      renders exactly one story via a headless single-run Playwright
      invocation, captures screenshot + accessibility-tree snapshot +
      console messages, writes them to `artifacts/ui-probe/<story>/`,
      shuts the server down, exits.
- [ ] 17.2 Same command accepts a route path (not just a story id) for
      probing a full page, not only isolated components.
- [ ] 17.3 Document in WS0's rule doc (or a new short note) that this is a
      bounded single-shot invocation, not a dev/watch server — explicit
      callout against AGENTS.md's "no long-lived dev/watch/serve from an
      agent" rule so future agents don't confuse the two.

**Acceptance:** a coding agent can run `ui-probe` against a story it just
changed and get a screenshot + DOM/a11y tree + console log back without a
human starting `bun run dev`; the process exits on its own within a bounded
timeout.

---

## WS18 — `test:affected` feedback command + reports

**Depends on:** WS1 (T0/T1 must be script-driven first); extended as later
tiers land, does not block on them. **Size:** M.
**Files:** new `scripts/ci/test-affected.mjs`, root `package.json` (new
`test:affected` script if a root script runner exists, else documented as
`bun run scripts/ci/test-affected.mjs`).

- [ ] 18.1 `git diff`-based change classification: touched
      `platform/src/**` → T0 unit + relevant T1 integration projects;
      touched `Comuki.Engine.Compute`/`Comuki.Host.Translator` →
      also T2 (fake mode); touched `dashboard/**` → FE unit + WS16
      storybook first batch; touched `agents/**` → agents/ bun test.
- [ ] 18.2 Emits the design.md JSON+markdown report, plus a one-line stdout
      verdict.
- [ ] 18.3 Document the "green gate before MR" rule
      (`.agents/rules/process/` — new short doc or an addition to an
      existing process rule file) referencing `test:affected` as the
      expected pre-MR command.

**Acceptance:** on a branch that only touches one module, `test:affected`
runs a strict subset (not the full matrix) and finishes in materially less
time than the full T0+T1 run, while still catching a deliberately
introduced regression in that module.
