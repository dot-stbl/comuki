# scripts/ci

CI/dev scripts shared between GitHub Actions and GitLab CI, and between CI
and a local/agent inner loop. Zero third-party dependencies — bun/node only,
runs on a bare checkout.

## `test-affected.mjs` — green gate before an MR

```bash
bun run test:affected                                    # plan + run, base = origin/master (falls back to master)
bun run test:affected -- --dry-run                        # print the plan only, run nothing
bun run test:affected -- --base feature/some-branch        # diff against a specific ref instead
bun run test:affected -- --include-integration              # also execute affected tests/integration/* projects
bun run test:affected -- --report-dir artifacts/my-report   # custom report location
```

Classifies `git diff` against the merge-base into affected targets and runs
them once, single-shot:

| Changed | Runs |
|---|---|
| `platform/**/*.cs` / `*.csproj` (also `tests/`, `tools/`) | Owning project → reverse ProjectReference closure (parsed from the .csproj files) → affected `tests/unit/*` projects |
| Anything under `platform/` | `tests/Comuki.Architecture.Tests` always (layer rules are solution-wide, not traceable through one project's references) |
| Same closure, `tests/integration/*` members | Only *executed* with `--include-integration` (needs Testcontainers/Podman); otherwise named in the plan/notes, never silently dropped |
| `dashboard/**` | `bun run typecheck` + `bun run lint` + `vitest related` (falls back to the full `vitest run` when a config file changed) |
| `cli/**` | `bun run test` |
| `agents/**` | `bun run test` |
| `scripts/**` | `node --test` over every `*.test.mjs` under `scripts/` |
| `openspec/**` | `openspec validate` scoped to the touched change(s)/spec(s) — never `--all` |
| Docs / repo config only | Nothing — reports `PASS 0/0` |

**Podman on Windows/WSL** — before `--include-integration`:

```bash
export DOCKER_HOST=npipe://./pipe/podman-machine-default
export TESTCONTAINERS_RYUK_DISABLED=true
```

Output: `report.json` + `report.md` under `--report-dir` (default
`artifacts/test-reports/affected/`), in the envelope shape documented in
`openspec/changes/add-agentic-test-contour/design.md` ("Report format for
agents"), plus a one-line stdout verdict (`PASS 11/12` / `FAIL 1/12 — see
report.md`).

Unit tests: `node --test scripts/ci/test-affected.test.mjs`.

## `dotnet-test.mjs` — the .NET tier runner `test-affected.mjs` calls into

```bash
node scripts/ci/dotnet-test.mjs --tier=unit --full
node scripts/ci/dotnet-test.mjs --tier=integration
```

Discovers and runs xUnit v3 (Microsoft Testing Platform) projects via
`dotnet run --project <csproj>` (MTP suites aren't discoverable by `dotnet
test`), parses each project's CTRF report, and writes the same report
envelope shape. `test-affected.mjs` reuses its exported `runProject` (spawn +
CTRF parse) for the exact, dependency-graph-derived project list it computes,
instead of re-implementing that path.

Unit tests: `node --test scripts/ci/dotnet-test.test.mjs`.

## `e2e-up.mjs` / `e2e-down.mjs` / `e2e-smoke.mjs` — WS13 hermetic compose stack

```bash
node scripts/ci/e2e-up.mjs                 # build comuki-test-fake-model:e2e
                                          # + compose build/up --wait
node scripts/ci/e2e-smoke.mjs              # login as bootstrap admin, create
                                          # project/connection/rule, POST signed
                                          # webhook, poll run, write report.json/.md
node scripts/ci/e2e-down.mjs               # compose down -v + remove bootstrap.json
```

Drives the **T3** hermetic end-to-end stack declared in
[`deploy/compose.e2e.yml`](../../deploy/compose.e2e.yml): Postgres + MinIO +
the Comuki host (built from the repo root) + a TestFakeModel reference
container + a TestFakePi-based worker image. Zero paid-model credentials
anywhere — TestFakePi replaces `pi` entirely
(`tests/tools/Comuki.TestFakePi/worker-test.Dockerfile`), so workers run
the whole webhook→run flow without ever calling a model.

The script reuses `buildEnvelope`, `formatVerdict`, `renderMarkdown` from
`scripts/ci/dotnet-test.mjs` for its report shape (tier `e2e-smoke`, mode
`fake`, one scenario entry `e2e-smoke`). `e2e-smoke.mjs` defaults to
`--assert-through=terminal`; the orchestrator may revise that default
during live validation per the WS13 brief.

Always wrap the three in a `try/finally` at the call site — `e2e-up.mjs`
intentionally does NOT auto-teardown on failure (it leaves the stack up
for post-mortem and prints every service's log tail).

The stack needs the host docker/podman socket bind-mounted into the
`comuki-e2e-host` container — see
[`.agents/rules/process/local-test-runtime.md` §Docker/Podman socket](../../.agents/rules/process/local-test-runtime.md)
and issue #153 (the `ComputeInstaller.cs` `DockerClientBuilder()` gap
that makes `DOCKER_HOST` an env-var trap).

Unit tests: `node --test scripts/ci/e2e-up.test.mjs scripts/ci/e2e-down.test.mjs scripts/ci/e2e-smoke.test.mjs`.

## `no-ai-attribution.mjs` — server-side no-AI-attribution gate

```bash
node scripts/ci/no-ai-attribution.mjs --range origin/master..HEAD
node scripts/ci/no-ai-attribution.mjs --range origin/master..HEAD --text-file pr-body.md
node scripts/ci/no-ai-attribution.mjs --report-dir artifacts/my-report
```

Scans a revision range — and optionally a free-form text body (PR/MR
description) — for AI authorship bylines and fails the build on any
hit. This is the non-bypassable server-side layer that backs
[`.agents/rules/process/no-ai-attribution.md`](../../.agents/rules/process/no-ai-attribution.md):
the local `commit-msg` hook covers only the message of one commit and
can be skipped with `git commit --no-verify`, but pushed history is
past it, so this gate runs over a diff range after the fact and fails
the build. The script imports `AI_VENDORS`, `AI_EMAIL_DOMAINS`,
`alternation`, and `stripAttribution` straight from
`scripts/commit-lint.mjs` — one source of truth, no second list of
vendor patterns to drift.

Unit tests: `node --test scripts/ci/no-ai-attribution.test.mjs`.

### GitLab (`deploy/hybrid/**`)

GitLab's `deploy/hybrid/**` overlay lives outside this repo — a human
coordinator applies the snippet below there by hand; do **not** edit
that path from here. The CI job mirrors the GitHub one above: an MR
job scans `base..head` plus the MR description; a push job scans
`before..after` without a description.

```yaml
no-ai-attribution:
  stage: test
  rules:
    - if: '$CI_PIPELINE_SOURCE == "merge_request_event"'
  script:
    - printf '%s' "$CI_MERGE_REQUEST_DESCRIPTION" > /tmp/mr-description.txt
    - >
      node scripts/ci/no-ai-attribution.mjs
      --range "$CI_MERGE_REQUEST_DIFF_BASE_SHA..$CI_COMMIT_SHA"
      --text-file /tmp/mr-description.txt
      --report-dir artifacts/test-reports/no-ai-attribution
  artifacts:
    when: always
    paths:
      - artifacts/test-reports/no-ai-attribution
    expire_in: 1 week

no-ai-attribution:push:
  stage: test
  rules:
    - if: '$CI_PIPELINE_SOURCE != "merge_request_event"'
  script:
    - >
      node scripts/ci/no-ai-attribution.mjs
      --range "$CI_COMMIT_BEFORE_SHA..$CI_COMMIT_SHA"
      --report-dir artifacts/test-reports/no-ai-attribution
  artifacts:
    when: always
    paths:
      - artifacts/test-reports/no-ai-attribution
    expire_in: 1 week
```

The coordinator should set `image:` / `stage:` / `GIT_DEPTH` (needs
full history so the diff base resolves — same `fetch-depth: 0`
reasoning as the GitHub job) to whatever the existing
`deploy/hybrid/ci.yml` conventions are. `$CI_COMMIT_BEFORE_SHA` is
all-zeros on a branch's first push — same edge case the GitHub job's
root-commit fallback handles; decide whether to mirror that fallback
or accept GitLab's own handling.
