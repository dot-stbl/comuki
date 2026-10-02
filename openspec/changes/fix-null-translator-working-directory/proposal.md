## Why

WS6 (#135) ran a real worker container end-to-end and observed the
Translator host crash on its first claim cycle. Root cause: the
in-memory config snapshot in `Comuki.Host.Translator/Program.cs`
(`TranslatorEnvironment.Snapshot`) writes every `COMUKI_*` env var onto
the config as a string, including the unset ones — so the absence of
`COMUKI_WORKING_DIRECTORY` (the worker image never stamps it; not part
of the per-claim env contract) became a literal `null` entry under
`Translator:WorkingDirectory`. Config binding then overwrote the
`Directory.GetCurrentDirectory()` default on `TranslatorOptions` with
`null`. `ProfilesProvider.PrepareAsync` calls `Path.Combine(null,
"profiles")` and throws `ArgumentNullException`; as the first
hosted-service exception, that throws `BackgroundServiceExceptionBehavior.StopHost`,
terminating the container before a single work item is processed.

The upstream fix `be2af0a0` (commit message "drop unset env entries
instead of binding null") already lands the smallest correct change:
filter the snapshot so unset env vars are *omitted*, letting every
option keep its default. This change retrofits the planning artifacts
that capture *why* and *what* that fix protects, closes the
integration-test workaround that masked it, and adds a regression
test so the env-binding footgun cannot return under any future
`COMUKI_*` addition. Coordinate with `harden-pi-worker-sandbox`
(#121), which gates worker image hardening — that change must not
break the "omit unset, keep default" contract this change locks in.

## What Changes

- **Document the env-snapshot contract change.** Unset `COMUKI_*` env
  vars MUST be omitted from the in-memory config snapshot rather than
  bound as `null`. The existing
  `Translator environment contract` requirement on `worker-runtime`
  is amended to spell this out (delta spec) and to forbid the
  `null`-overwrites-default failure mode for any future addition to
  the contract.
- **Add a regression test.** A unit test on the env-snapshot helper
  proves that an unset `COMUKI_WORKING_DIRECTORY` produces no
  `Translator:WorkingDirectory` entry (and therefore does not bind
  `null`), and a `ProfilesProvider` test proves the provider does not
  throw on a fresh host (default `WorkingDirectory`) when no
  `COMUKI_PROFILES_*` source is configured.
- **Close the integration-test workaround.** The end-to-end
  `AgentLoopHarness.StartWorkerAsync` (T2a, `tests/integration/...`)
  currently sets `COMUKI_WORKING_DIRECTORY=/work` to mask the bug.
  Remove the workaround entry once the regression test confirms the
  default path works without it; update the long-form comment to
  point at this change as the closure.

## Capabilities

### New Capabilities

None. The contract is small and lives entirely inside the existing
`worker-runtime` capability — adding a sibling capability would split
what is one behavior across two specs.

### Modified Capabilities

- `worker-runtime`: the **Translator environment contract** requirement
  gains a clause that unset `COMUKI_*` entries SHALL be omitted from
  the in-memory snapshot (defaults preserved), with a scenario that
  proves the absence of `COMUKI_WORKING_DIRECTORY` leaves
  `TranslatorOptions.WorkingDirectory` at its `Directory.GetCurrentDirectory()`
  default. The contract's existing scenario for "Container env becomes
  config" is tightened to assert the same omit-on-empty semantics.

## Impact

- `platform/src/host/Comuki.Host.Translator/Program.cs` — already
  patched by `be2af0a0`; the regression test is the only added code
  this change ships on the platform side.
- `tests/unit/Comuki.Host.Translator.Unit.Runtime/` — add the
  snapshot-omits-unset regression test and the
  `ProfilesProvider` "default working directory" test.
- `tests/integration/Comuki.EndToEnd.AgentLoop/AgentLoopHarness.cs` —
  drop the `COMUKI_WORKING_DIRECTORY=/work` workaround entry and
  rewrite the surrounding comment to point at this change.
- `openspec/specs/worker-runtime/spec.md` — apply the delta on apply.
  No edit to `openspec/specs/` during this change (planning only).
- `deploy/hybrid/worker.Dockerfile` — out of scope: the
  freeze-zone rule forbids touching deploy artefacts from a
  non-engineering run; the existing image's omission of
  `COMUKI_WORKING_DIRECTORY` is correct now that the in-memory
  snapshot omits nulls. Coordinate with `harden-pi-worker-sandbox`
  if it later stamps the var — the contract change keeps that
  additive.

## Non-goals

- Re-engineering the env-snapshot helper into a generic
  config-binding abstraction. The explicit dictionary stays — it's
  the contract surface, easy to read.
- Touching `deploy/hybrid/worker.Dockerfile` to set
  `COMUKI_WORKING_DIRECTORY=/work` explicitly. The current
  "don't stamp it; let the default ride" is now safe and is what
  the regression test pins.
- Changing `WorkerImage` / `ProfilesPath` / `ProfilesGitUrl` /
  `ProfileKey` / `ProfilesRef` default values — those defaults are
  fine; only the "unset env var must not bind `null`" rule changes.
- A general "no-null-config" refactor across other hosts.
  `Comuki.Host` has its own DB-resolution contract; it already
  fails boot on a missing value, which is a different shape and
  not in scope here.
- Cowork warm-slot stamp-on-start semantics (`harden-pi-worker-sandbox`
  §D3). The "keep defaults when env unset" rule is independent of
  whether keys are stamped at container start or at claim time.