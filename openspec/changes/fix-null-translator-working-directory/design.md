## Context

`Comuki.Host.Translator/Program.cs` builds an in-memory config
dictionary (`TranslatorEnvironment.Snapshot`) that maps every
`COMUKI_*` env var onto a `Translator:<Option>` key, then pushes it
into the configuration tree *after* `UseComukiBootstrap()` so it
wins over any `config.toml` value. The dictionary was built
without filtering: `Environment.GetEnvironmentVariable("COMUKI_…")`
returns `null` for unset names, and the dictionary happily stored
the `null` under the right key. The Microsoft.Extensions.Options
binder then bound that `null` onto the typed option — overwriting
`WorkingDirectory`'s `Directory.GetCurrentDirectory()` default,
then overwriting `ProfilesPath` / `ProfilesGitUrl` / `ProfilesRef`
/ etc. when those were unset. `ProfilesProvider.PrepareAsync`'s
unguarded `Path.Combine(opts.WorkingDirectory, "profiles")` then
threw `ArgumentNullException` on the first claim cycle, and as the
first hosted-service failure it propagated to
`BackgroundServiceExceptionBehavior.StopHost`, killing the
container.

The upstream fix `be2af0a0` adds a `.Where(pair => pair.Value is
not null).ToDictionary(...)` post-filter in `Snapshot` so unset
entries are dropped. With the filter, the binder never sees a
`null` for an unset env var and every option keeps its declared
default. The change is small, the regression risk is low, and the
shape of `Snapshot` is preserved (it is still the explicit
env→config surface, easy to read in one place).

## Goals / Non-Goals

**Goals:**

- One **filter at the snapshot boundary** keeps the contract
  trivially correct: nothing leaves the snapshot that is not a
  real string. No per-option null guards in `ProfilesProvider` or
  elsewhere — the boundary fix forecloses the failure shape for
  every option, not just `WorkingDirectory`.
- One **regression unit test** on the snapshot helper proves the
  filter (unset ⇒ key absent from the dictionary).
- One **regression unit test** on `ProfilesProvider` proves
  `Path.Combine(opts.WorkingDirectory, "profiles")` does not throw
  when the default working directory is in effect and no
  `COMUKI_PROFILES_*` source is configured (today the
  "MissingSourceLogsWarningAndSkipsAsync" test still constructs
  an explicit working dir; a sibling test that exercises the
  default path covers the regression).
- Drop the `COMUKI_WORKING_DIRECTORY=/work` entry from
  `AgentLoopHarness.StartWorkerAsync` and rewrite its long comment
  to point at this change as the closure.

**Non-Goals:**

- Touching `deploy/hybrid/worker.Dockerfile`. The freeze-zone rule
  forbids it from a non-engineering run, and the current image's
  absence of `COMUKI_WORKING_DIRECTORY` is the right behaviour
  now that the snapshot omits nulls.
- Refactoring `TranslatorEnvironment.Snapshot` into a generic
  config-binding abstraction (a convention-driven binder, a
  reflection walker over `TranslatorOptions`, etc.). The explicit
  dictionary is the contract; readability wins over DRY for ten
  options.
- Adding `null` guards inside `ProfilesProvider`. The boundary
  fix means the provider never sees `null`. Duplicating the guard
  would be belt-and-braces for no extra safety and would obscure
  where the contract lives.
- Changes to the `Required` validation on orchestrator URLs / token
  / profile key. They still fail the boot when unset; the contract
  change only relaxes the **non-required** options' "default
  survives" rule.

## Decisions

### D1. Filter at the snapshot, not at the provider

The `Where` filter in `Snapshot` is the **one** place the
null-binding footgun is foreclosed. Every option below — required or
not — gets the same protection. Pushing the guard into
`ProfilesProvider` (e.g. `WorkingDirectory ?? Directory.GetCurrentDirectory()`)
would only fix that one symptom; the next option that ships (a
future `Translator:SandboxMode`, `Translator:FixturePath`, ...)
would re-introduce the same null-binding failure unless every
consumer added the same guard.

**Alternative:** convention-driven env→options binding via
reflection over `TranslatorOptions`. Rejected — the explicit
dictionary is the documented env contract and is easier to diff
than a binder walk. The shape of `Snapshot` is also where
`harden-pi-worker-sandbox`'s "stamp-on-claim" code will land; we
want it readable.

### D2. Drop unset entries; do NOT coerce null to empty string

The filter is `pair.Value is not null`. It does **not** map
`null → ""`, because an empty string would still bind onto a
non-nullable string option (the binder accepts empty), and
`string.IsNullOrEmpty(opts.ProfilesPath)`-style checks would then
look like "user explicitly cleared it" rather than "default
applies". Omitting the key keeps the option at its declared
default; the framework's `Bind(...).ValidateDataAnnotations()` +
`ValidateOnStart()` pipeline only sees the option's default +
config.toml + env vars that are actually set.

**Alternative:** keep the entry but write `""`. Rejected — the
two semantics ("unset" vs "user-set empty") should not collapse.

### D3. Required options keep their boot-fail behaviour

`Required` / `MinLength` on `OrchestratorBaseUrl`, `OrchestratorGrpcUrl`,
`WorkerToken`, `ProfileKey`, `ProfilesRef`, `WorkerImage` stays.
`ValidateOnStart` still throws naming the missing option. The
filter only relaxes the "default survives" behaviour for the
options that **have** defaults (`PiExecutable`, `WorkingDirectory`,
`ProfilesPath`, `ProfilesGitUrl`, `ClaimPollInterval`,
`HeartbeatInterval`). A future option without a default MUST be
`[Required]` and will still fail the boot when unset.

**Alternative:** make every required option default-skip too.
Rejected — the work doesn't boot without an orchestrator to call.

### D4. Regression tests live in the existing unit projects

- `tests/unit/Comuki.Host.Translator.Unit.Runtime/`:
  - `TranslatorEnvironmentSnapshotShould` — new test class
    covering: empty env → empty snapshot; one env set → one
    entry with the same key/value; full env → all ten entries
    with values, none null.
  - `ProfilesProviderShould` — add
    `WorkingDirectoryDefaultDoesNotThrowAsync` exercising
    `PrepareAsync` with `WorkingDirectory = Directory.GetCurrentDirectory()`
    and no profiles source; expects the documented warning + no
    `profiles/` directory created. Sits next to
    `MissingSourceLogsWarningAndSkipsAsync`.

### D5. Integration test workaround removal is the last step

`tests/integration/.../AgentLoopHarness.cs` currently stamps
`COMUKI_WORKING_DIRECTORY=/work` to mask the bug, with a
multi-line comment explaining the production gap. Removal happens
**after** the new unit tests land — that way the next CI run
either passes (snapshot filter works, default survives, harness
removes the stamp, end-to-end test still passes) or it doesn't
(workaround removal reverted, comment restored), and the diff
makes the closure visible. The comment is rewritten to cite this
change as the closure rather than describing the original bug.

**Alternative:** leave the workaround and the comment. Rejected —
the harness's job is to mirror production; production now runs
without the stamp, so the harness should too.

### D6. No deploy-zone edits in this change

`deploy/hybrid/worker.Dockerfile` stays as it is. The freeze-zone
list in the worktree rules forbids editing it from a non-engineering
run, and the change is not the right policy surface — `deploy/` is
where the contract is *exercised*, not where it is *defined*. If
`harden-pi-worker-sandbox` later chooses to stamp
`COMUKI_WORKING_DIRECTORY=/work` explicitly on the worker image
(for clarity in the image-level env block), the new contract is
additive — the filter would drop nothing new, the option would
just bind to `/work` instead of the directory default. Both are
correct under this change.

## Risks / Trade-offs

- **[Risk] A future option without a default gets added to the
  snapshot without `[Required]`.** → Mitigation: the regression
  test on the snapshot is the safety net; `ValidateOnStart` will
  catch the wrong option at boot, but the spec scenario "An unset
  required option still fails the boot" keeps that path tested.
  Add an ADR note in `TranslatorEnvironment`'s doc comment that
  every entry in `Snapshot` MUST have either a default on the
  option side or `[Required]` validation.
- **[Risk] A future change wraps the snapshot in a binder that
  bypasses the filter.** → Mitigation: the regression test on the
  snapshot helper pins the shape; if a refactor replaces
  `Snapshot` with a reflection walker, the test still validates
  the resulting dictionary against the same input env.
- **[Risk] Cowork `worker-pools` stamps keys per-execution and
  bypasses the snapshot.** → Mitigation: harden-pi-worker-sandbox
  §D3 already plans to keep stamp-on-claim-and-revoke as the
  model; per-slot stamp goes through the same env→options path
  and the snapshot filter is invariant under the call shape.
- **[Trade-off] An explicit `COMUKI_WORKING_DIRECTORY=""` would
  now be treated as "unset" rather than "user wants an empty
  path".** → Acceptable — there is no supported scenario for
  "empty working directory", and the alternative (binding an empty
  string onto `WorkingDirectory`) would make the next
  `Path.Combine` explode differently.

## Migration Plan

Additive. No runtime config change for existing deploys:

- **Existing deployments** continue to boot. The filter only
  changes behaviour when an env var that *would have been bound
  to null* is unset — and an unset env var has never been a
  supported signal for "I want this option to be null".
- **Rollback** is `git revert` on `be2af0a0` plus this change.
  The harness workaround (`COMUKI_WORKING_DIRECTORY=/work`) keeps
  the regression-test suite green even with the snapshot filter
  removed, masking the rollback — the production crash would
  re-surface in a real worker container.
- **Coordinated with harden-pi-worker-sandbox (#121):** that
  change's mint+stamp code paths write through `ProcessStartInfo.Environment`,
  not through the snapshot; the contract is independent of where
  the keys are stamped. The only coordination point is
  *cross-linking* the two changes in `harden-pi-worker-sandbox`'s
  follow-ups (§D6 / COORDS).

## Open Questions

None that change specs, the chosen approach, or the task
breakdown. Two deferred items, neither spec-blocking:

- Whether the worker image should *additionally* stamp
  `COMUKI_WORKING_DIRECTORY=/work` for self-documentation. Out of
  scope here (deploy-zone edit, image policy is
  harden-pi-worker-sandbox's call). The new contract is additive
  either way.
- Whether to lift the env→config bridge to a shared helper used
  by other hosts (`Comuki.Host`, future `Comuki.Host.Mcp`). Out
  of scope — the contract change is local to the Translator; a
  shared helper is a follow-up after the host-side env contracts
  are pinned.