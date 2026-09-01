# Build and CI Specification

## Purpose

Defines the verification gate: the single solution build that compiles, runs analyzers and enforces format (`dotnet build comuki.slnx -c Debug`), the format-gate mechanics and escape hatches, the analyzer policy, and the GitHub Actions pipeline.

## Requirements

### Requirement: One command build gate

`dotnet build comuki.slnx -c Debug` SHALL be the entire backend verification: compilation, analyzers (warnings-as-errors), and the format check, all in one invocation identical to CI. Exit 0 means the code meets the standard; any non-zero exit means the change is not done. Building a single project bypasses the format gate (the gate lives in a solution-order project), so per-project builds are inner-loop only.

#### Scenario: CI parity
- **WHEN** CI builds the solution
- **THEN** it runs the same command a developer runs locally — no extra flags

### Requirement: Format gate runs once per solution build

The format verify SHALL live in a dedicated no-dependency build-tools project placed first in the solution, so MSBuild's topological order runs the target exactly once per solution build (a `Directory.Build.targets` target would fire once per project). Drift FAILS the build.

#### Scenario: One invocation
- **WHEN** the solution builds
- **THEN** the verify target runs once, before other projects build

### Requirement: Format gate mechanics

The gate SHALL run `dotnet format comuki.slnx --verify-no-changes --severity hidden --no-restore` with excludes: `**/Migrations/**`, `**/*ModelSnapshot.cs`, `**/*.Designer.cs`, and diagnostics `IDE0010`/`IDE0072` (the destructive add-missing-cases fixers that have inserted `NotImplementedException` switch arms). Because the .NET 10 SDK returns exit 0 even when drift is found under cmd/PowerShell, the gate SHALL redirect output to `obj/format-verify.log` and count diagnostic lines with a regex; a non-zero count fails (the log directory is created first — on a fresh clone the redirection would otherwise fail silently and the gate would pass vacuously).

#### Scenario: Drift fails the build
- **WHEN** a file has trailing whitespace or style-rule drift
- **THEN** the build fails naming the violation count and pointing at the log

#### Scenario: Migrations never hand-formatted
- **WHEN** tool-generated migration files drift from formatter style
- **THEN** the gate ignores them via the excludes

### Requirement: Format gate escape hatches

Per-invocation, inner-loop-only escapes SHALL exist: `-p:DisableFormatOnBuild=true` skips the gate entirely; `-p:FormatOnBuildTreatAsWarning=true` downgrades drift to a warning. Neither is sanctioned for a "done" build. Fixing drift: `dotnet format comuki.slnx --severity hidden` — then REVIEW THE DIFF, the formatter must never be trusted blindly.

#### Scenario: Hotfix escape
- **WHEN** a blocking fix must ship and the gate interferes
- **THEN** the escape flags skip/downgrade for that invocation only, noted in the commit

### Requirement: Analyzer policy (surgical set)

The analyzer set SHALL be IDE code-style plus the NetAnalyzers CA Security category ONLY: `AnalysisMode=None` with `AnalysisModeSecurity=All`, individual CA rules opt-in via `.editorconfig`. Meziantou (MA), Roslynator (RCS) and the bulk of VSTHRD are REMOVED (noise reduction) — `Microsoft.VisualStudio.Threading.Analyzers` remains as the sole analyzer package with a surgical severity set:

- `VSTHRD200` (Async suffix), `VSTHRD002` (no `.Result`/`.Wait()`/`GetAwaiter().GetResult()`), `VSTHRD104` (offer CancellationToken) — error
- `VSTHRD103` (call async counterpart) — none: the deliberate sync read paths it flags
- `VSTHRD110` — warning
- `VSTHRD200` in top-level `Program.cs` — none: the only sanctioned exemption (entry points cannot take the suffix)

`TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are global; `CS1591` (XML doc comments) is suppressed globally and added per-area as code lands.

#### Scenario: Blocking call rejected
- **WHEN** production code awaits via `.Result`
- **THEN** VSTHRD002 fails the build

#### Scenario: Program.cs entry exempt
- **WHEN** a top-level entry point returns a Task without the Async suffix
- **THEN** VSTHRD200 does not fire in Program.cs

### Requirement: Destructive style fixers off

`IDE0010` (add missing switch cases) and `IDE0072` (add missing enum member) SHALL be `none` everywhere — their code fixers insert `NotImplementedException` arms. `IDE0058` (unused expression) SHALL be `none` — its fixer inserts `_ =` discards the codebase forbids. `IDE0022` (expression-bodied method) SHALL be false: methods use block bodies, and the format gate converts any expression-bodied method back.

#### Scenario: No discard insertion
- **WHEN** a method call's result is intentionally unused
- **THEN** no analyzer demands a `_ =` discard

### Requirement: Test project coverage floor

Test projects (`IsTestProject=true`) SHALL collect line coverage (cobertura + opencover) with a 70% line threshold on the total — the target floor, not 80+.

#### Scenario: Coverage below the floor
- **WHEN** a test project's total line coverage drops under 70%
- **THEN** the coverage run reports the threshold failure

### Requirement: CI jobs

CI SHALL run on pushes to master and pull requests (concurrency-cancelled per ref, read-only contents):

- `build backend` — checkout, .NET 10 setup, NuGet cache keyed on package pins + csproj hashes, `dotnet restore comuki.slnx`, `dotnet build comuki.slnx -c Debug --no-restore`
- `test backend` — a matrix (fail-fast off) over test projects, each run via `dotnet run --project <csproj> -c Debug` because xUnit v3 rides Microsoft Testing Platform and `dotnet test` cannot discover it; fresh runners rebuild the project graph
- `build frontend` — dashboard: bun install, `bun run typecheck`, `bun run lint`, `bun run test`; NON-BLOCKING (`continue-on-error`) while the dashboard lint config is mid-migration (known eslint plugin breakage; typecheck and vitest are green)

#### Scenario: MTP invocation
- **WHEN** CI runs a backend test project
- **THEN** it uses `dotnet run --project`, never `dotnet test`

#### Scenario: Frontend lint cannot block
- **WHEN** the dashboard lint step fails on the known config breakage
- **THEN** the workflow still succeeds; the step is explicitly non-blocking until the config is fixed

## ADAPTER Notes

No docker image build in CI yet — the worker image (deploy/worker.Dockerfile) is built locally/on-demand. The dashboard gate becomes blocking when its eslint config is repaired (the `continue-on-error` line carries the removal note).
