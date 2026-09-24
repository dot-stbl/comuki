# `Comuki.AgentEval` — WS10 golden-task eval harness

A standalone .NET CLI tool that drives the real `pi` binary against
the `tests/fixtures/target-repos/small-repo` (or any other target-repo)
fixture, backed by an in-process fake-model server
(`Comuki.TestFakeModel`'s `FakeModelServer` for `mode: fake` or
`CassetteModelServer` for `mode: replay`/`mode: live`), and judges
the result deterministically plus — when a live LLM-as-judge
upstream is configured — with a rubric-weighted overall score.

## CLI usage

```bash
dotnet run --project tests/tools/Comuki.AgentEval -- \
    --mode=fake|replay|live \
    --corpus=<dir> \
    --budget-usd=<n> \
    [--out=<basePath>] \
    [--history=<path>] \
    [--filter=<scenarioName>] \
    [--trend]
```

- `--mode=fake` — scripted in-process fake model, zero network.
- `--mode=replay` — committed cassette served byte-for-byte.
- `--mode=live` — recording proxy pointing at the upstream
  `COMUKI_LIVE_MODEL_BASE_URL`. When that env var is unset, prints a
  clean skip message and exits 0 (never fail the command just because
  the live target isn't configured).
- `--corpus` — repo-relative path to the corpus directory; default
  `tests/fixtures/scenarios/agent-eval`.
- `--budget-usd` — process-wide ceiling; combines with each scenario's
  own `assertions.cost.maxUsdMicros` via `BudgetCap.Resolve` (smaller
  wins). Unset = unlimited (also reads `COMUKI_LIVE_BUDGET_MAX_USD`).
- `--out` — base path (no extension) for the JSON+markdown report.
  Default `artifacts/agent-eval/<UTC-yyyyMMddTHHmmssZ>-report`.
- `--history` — JSONL history file path. Default
  `artifacts/agent-eval/history.jsonl`.
- `--filter` — exact-match filter on entry `name`.
- `--trend` — if the only flag, reads `--history` and prints a plain-text
  trend table.

## Corpus format

Each scenario is a standard `.scenario.yaml` (the shape
`Comuki.AgentTest.Runner.Scenarios.ScenarioLoader` validates) plus a
top-level `eval:` extension block:

```yaml
schemaVersion: 1
name: <kebab-case-unique>
description: ...
ticket: { source, title, body, labels, targetRepo: { fixture, ref } }
worker: { image, profileKey, profilesRef }
model:  { mode, fakeScript? | cassette? }
eval:
  difficulty: trivial|easy|medium|hard
  expectedOutcome: ...
  allowedFiles: [...]
  maxToolCalls: <n>
  # Optional LLM-as-judge rubric:
  rubric:
    criteria:
      - id: <kebab-case>
        description: ...
        weight: <n>
    minScore: <0..1>
```

See `tests/fixtures/scenarios/agent-eval/` for worked examples at every
difficulty level — trivial (`null-guard-simple`), easy
(`extract-magic-number`), medium (`add-validation`), hard
(`new-helper-file`, `ambiguous-symptom`). The rubric format is
demonstrated on `ambiguous-symptom.scenario.yaml`.

## How judges combine into `Passed` / `QualityScore`

1. **Deterministic gate** — the entry fails if any
   deterministic verdict with `Passed != null` has `Passed == false`.
   Verdicts with `Passed == null` are n/a (e.g. `tests-pass` with no
   `testCommand` declared) and never block.
2. **LLM-as-judge rubric** — when `eval.rubric` is declared and a live
   judge env is configured (`COMUKI_LIVE_MODEL_BASE_URL`), the judge
   prompt is sent through `HapyLlmJudgeClient` and parsed by
   `JudgeVerdictParser`. The parser recomputes `OverallScore` from the
   per-criterion scores (the model's self-reported value is discarded)
   and validates the criterion-id set matches the rubric exactly.
3. **`Passed`** — `DeterministicPass` is a hard gate; a rubric-declared
   entry with `judge.Kind == Error` fails; a rubric-declared entry
   with `judge.Kind == Skipped` (no live env) passes when deterministic
   passes; a no-rubric entry passes when deterministic passes.
4. **`QualityScore`** — `DeterministicPass ? 1.0 : 0.0` half plus
   `judge.OverallScore * 0.5` half (when scored), else
   `DeterministicPass ? 1.0 : 0.0`. Deliberately simple v1.

## Manual-only

`Comuki.AgentEval` is a manual/local entry point. It is NEVER invoked
from `.github/workflows/ci.yml` or `deploy/hybrid/ci.yml` — CI wiring
is a separate, already-closed workstream (WS12). A human (or an
explicit qa:agent-eval job) drives it.

## Cross-reference

> Cross-reference: `openspec/changes/add-mission-cowork` tasks 7.8 / 15.3 / 18.3 cover a DIFFERENT eval surface (memory/template evals, not agent-loop golden tasks) — not folded into this project; see that change's `tasks.md` for those.
