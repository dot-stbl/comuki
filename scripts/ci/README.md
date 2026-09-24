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
