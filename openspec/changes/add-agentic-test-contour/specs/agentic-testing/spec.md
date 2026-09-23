## Purpose

Defines the agentic test contour: a declarative scenario format for the
ticket → brain → plan → workers-in-containers → result → sync-back chain,
executed in fake/replay/live modes against a shared fake-model harness; the
T1–T4 tiers built on it (integration foundation, agent-loop E2E, hermetic
compose E2E, live evals + agent-QA); Storybook-based UI testing plus an
agent-facing UI probe; and the `test:affected` feedback loop for coding
agents working on this repo. This capability does not own CI job placement
or the build gate — see `build-and-ci` for those.

## ADDED Requirements

### Requirement: One scenario format, three execution modes
A scenario SHALL be a single declarative file (a ticket fixture, a model
reference, an expected trajectory, and assertions) executable unchanged in
three modes: `fake` (a scripted fake model, deterministic, no network),
`replay` (a recorded cassette served byte-for-byte), and `live` (a real
model reached through the hapy gateway). Switching modes SHALL require only
changing the scenario's `model.mode` field, not rewriting the scenario.

#### Scenario: Same scenario, three modes
- **WHEN** a scenario authored for `fake` mode is re-run with `model.mode: replay` against a recorded cassette for the same scenario name
- **THEN** the same assertions evaluate against the replayed run without editing the scenario's ticket, trajectory, or assertion blocks

#### Scenario: Mode mismatch is explicit
- **WHEN** a scenario declares `model.mode: replay` but no cassette exists at the referenced path
- **THEN** the runner fails before attempting the run, naming the missing cassette

### Requirement: Fake-model server speaks both wire shapes
A fake-model HTTP server SHALL implement both the Anthropic Messages
(`POST /v1/messages`, including `stream: true` SSE) and OpenAI
chat-completions (`POST /v1/chat/completions`, including SSE chunks) wire
shapes, in fake, replay, and record modes. It SHALL be usable in-process
from an xUnit fixture and as a standalone container image, backed by the
same implementation in both forms.

#### Scenario: Real pi reaches the fake model
- **WHEN** the real `pi` binary is started with `ANTHROPIC_BASE_URL` pointed at the fake-model server in fake mode
- **THEN** pi completes a scripted exchange exactly as it would against the real Anthropic API, with no code path aware the upstream is fake

#### Scenario: Same binary, two forms
- **WHEN** the fake-model container image is started with the same fake script as the in-process fixture
- **THEN** it serves identical responses for the same requests

### Requirement: Cassettes are redacted before they are written
A recorded cassette SHALL classify every response field before writing it:
allowlisted fields (text, tool_use, usage, stop_reason) are kept, all other
headers are stripped except `content-type`, and any value matching a known
secret pattern (API key prefixes, bearer tokens, minted virtual-key shape,
or a name on the redaction denylist) is replaced. A field the classifier
cannot categorize SHALL cause the write to fail rather than default to
"kept."

#### Scenario: Unclassified field blocks the write
- **WHEN** a recorded response contains a field the redaction classifier does not recognize
- **THEN** the cassette write fails and no partially-redacted file is left on disk

#### Scenario: Secret never reaches the cassette
- **WHEN** a recorded response header or body contains a bearer token or minted virtual key
- **THEN** the written cassette contains a redaction marker in its place, never the original value

### Requirement: Replay fails loudly on a shape mismatch
When serving a cassette in replay mode, a request that does not match the
next expected cassette entry's structural predicate SHALL fail the
scenario with a typed mismatch error. Replay SHALL NOT silently fall
through to a default or partial response.

#### Scenario: Stale cassette caught
- **WHEN** the agent's request shape changes (a different tool sequence) after a cassette was recorded
- **THEN** replay fails on the first mismatched exchange instead of serving an unrelated recorded response

### Requirement: T1 integration runs on one shared, reset database
Integration test classes within one test-run process SHALL share one
Postgres instance via a collection fixture with all module schemas migrated
once, and SHALL reset between tests via `Respawn` (`TRUNCATE`, not
`MigrateAsync`). A test class SHALL NOT construct its own
`PostgreSqlBuilder` container when the shared fixture is available for its
module.

#### Scenario: Cross-test isolation without a fresh container
- **WHEN** two test classes in the same collection run back to back
- **THEN** the second test observes a clean schema state via `Respawn` reset, not leftover rows from the first, and no new container was started for the second class

### Requirement: T2a proves container lifecycle without a model
An agent-loop end-to-end suite SHALL exercise seed → real webhook →
run/work-item creation → real queue claim → a real container provisioned
through `Comuki.Engine.Compute` → gRPC stream → journal → artifact, using
`TestFakePi` in place of `pi`, with no model call involved. This closes the
gap where `TranslatorE2EShould` proves the gRPC/journal path but not real
Compute provisioning.

#### Scenario: Real container observed running
- **WHEN** the T2a suite runs a scenario
- **THEN** the work item completes through a container actually started by the Docker compute provider, not a bare subprocess

### Requirement: T2b proves worker-sdk hook enforcement in situ
A second agent-loop mode SHALL run the real `pi` binary (not `TestFakePi`)
inside the same provisioned container, pointed at the fake-model server,
so that `agents/comuki-worker-sdk` lock, skill, and MCP enforcement is
observed operating on a live tool-call stream rather than asserted only at
the unit level. A scenario's `expectedTrajectory.forbiddenTools` SHALL be
checked against the tools pi actually attempted.

#### Scenario: Denied tool call is observed, not assumed
- **WHEN** a scenario's fixture repo has a lock rule denying edits to a path and the fake-model script scripts pi to attempt that edit
- **THEN** the T2b run asserts the edit was denied at tool-call time and the run's final diff does not contain it

### Requirement: T3 is a hermetic compose stack requiring no live model key
A docker-compose target SHALL boot postgres+pgvector, MinIO, the host
image, and a worker image with the fake-model image swapped in for the
model upstream, drive the full ticket→brain→plan→worker→result→sync-back
chain through the real REST/webhook surface, and tear down cleanly,
without requiring a live Anthropic/OpenAI key.

#### Scenario: No key, still runs
- **WHEN** the T3 compose target is started with no `ANTHROPIC_API_KEY` or equivalent set in the environment
- **THEN** a seeded scenario still completes through the fake-model image

### Requirement: Live evals and agent-QA are manual-trigger and budget-capped
Any execution against a real model outside the fake/replay modes (T4 golden-
task evals, cassette re-recording, agent-QA runs) SHALL require an explicit
manual trigger (a manual CI job or a local command) and SHALL enforce a
per-run USD budget ceiling independent of the target project's own budget
configuration. None of these SHALL run automatically on every push or pull
request.

#### Scenario: Budget exceeded aborts the run
- **WHEN** a live-mode run's accumulated cost crosses its declared `budget.maxUsd`
- **THEN** the run aborts before making a further model call and the report records the abort reason

#### Scenario: Not triggered by a push
- **WHEN** a commit is pushed to a branch or merge request
- **THEN** no live-mode scenario, cassette re-record, or agent-QA profile runs as a side effect

### Requirement: T4 eval harness is named distinctly from the status-machine replay tester
The golden-task, model-quality eval harness introduced by this capability
SHALL be a separate tool from `Comuki.Engine.Orchestration.Unit.Eval`
(which remains the deterministic status-machine golden-file replay tester)
and SHALL NOT reuse its name, namespace, or `tests/unit/` location. It
SHALL score runs against a rubric via an LLM judge and record quality and
cost metrics per run in an appendable history.

#### Scenario: No terminology collision
- **WHEN** a developer greps the repo for "Eval"
- **THEN** the status-machine replay tester and the golden-task model-quality harness are unambiguously distinguishable by name and location

### Requirement: Agent-QA profile exercises the running system read-mostly
A control-plane worker profile SHALL be able to exercise Comuki's own
CLI/API/dashboard surface against a running instance and file findings as
labeled GitHub issues, following the same profile/skill/verdict structure
already used by `pr-review`. It SHALL operate read-mostly against the
target system (no destructive mutation of production data) and SHALL only
run on manual trigger.

#### Scenario: Findings filed, not silently dropped
- **WHEN** the agent-QA profile completes a run and finds a reproducible defect
- **THEN** a labeled GitHub issue with evidence (steps, screenshot or API trace) is filed, not just logged to a transcript

### Requirement: Storybook stories carry interaction, visual, and a11y coverage
A story SHALL be able to declare a `play` function exercised as an
interaction test, a visual snapshot compared against a committed baseline
per light/dark theme, and an axe accessibility check, all runnable in one
non-interactive pass against Storybook's static build output (no dev/watch
server required to execute the checks).

#### Scenario: A11y violation is attributed
- **WHEN** a story's rendered output has an accessibility violation
- **THEN** the report names the violated rule and the story id, not just a pass/fail bit

#### Scenario: Visual regression caught
- **WHEN** a story's rendered output changes beyond the documented tolerance from its committed baseline
- **THEN** the one-shot run reports a visual diff for that story and theme

### Requirement: UI probe renders one story or page without a dev server
An agent-facing command SHALL render a single named story or route in a
real browser in one bounded, non-interactive invocation and return a
screenshot, a DOM/accessibility-tree snapshot, and captured console
messages, without requiring a developer to have started `bun run dev` or
any other long-lived server. The command SHALL start any server it needs
internally and SHALL shut it down before exiting.

#### Scenario: Self-contained verification
- **WHEN** an agent has just changed a component and invokes the UI probe against the corresponding story
- **THEN** it receives a screenshot, DOM/a11y snapshot, and console log for that change without any other process already running

#### Scenario: Process exits on its own
- **WHEN** the UI probe command completes its single render
- **THEN** the process exits without requiring the caller to send an interrupt signal

### Requirement: `test:affected` gives one command and one report shape
A single command SHALL classify a working tree's changes and run the
narrowest correct set of tiers (T0 always; T1/T2/FE/Storybook tiers when
the touched paths warrant them), emitting one JSON report and one markdown
report generated from the same data, plus a one-line stdout verdict. This
command SHALL be the expected pre-MR gate for a coding agent working on
this repo.

#### Scenario: Scoped run on a single-module change
- **WHEN** a change touches only one backend module's files
- **THEN** `test:affected` runs that module's unit and integration suites without running the full matrix

#### Scenario: Report is self-sufficient
- **WHEN** a coding agent reads the markdown report after a failing run
- **THEN** it can locate the failing scenario or test and its artifact paths without re-running anything

### Requirement: Local container runtime is documented and scripted
Running T1–T3 locally on Windows/WSL without Docker SHALL be supported
against Podman through a documented helper that surfaces the required
environment (`DOCKER_HOST` pointed at the Podman machine pipe, Ryuk
disabled) rather than requiring undocumented tribal knowledge.

#### Scenario: Fresh machine, no manual troubleshooting
- **WHEN** a developer follows the local container-runtime helper's printed instructions on a machine with Podman but not Docker
- **THEN** the T1 shared-fixture integration suite runs without manual Testcontainers configuration
