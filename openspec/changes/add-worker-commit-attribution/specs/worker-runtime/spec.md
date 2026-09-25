## ADDED Requirements

### Requirement: Workspace preparation installs commit-attribution hooks (Layer 1)

The Translator loop's existing "prepare profiles material" step
(`step 2` of the "Translator loop" requirement) SHALL be extended
additively by a sibling preparer that runs alongside `ProfilesProvider`,
not by modifying `ProfilesProvider` itself. The sibling preparer SHALL:

1. Set the source workspace's git identity via `GIT_AUTHOR_NAME`,
   `GIT_AUTHOR_EMAIL`, `GIT_COMMITTER_NAME`, `GIT_COMMITTER_EMAIL` env
   vars exported into the agent child process. The values are
   resolved by `IRepositoryBotIdentity.Resolve(...)` (the
   `commit-attribution` capability's port) before the agent spawns.
2. Install a `prepare-commit-msg` git hook at
   `<workspace>/.git/hooks/prepare-commit-msg` whose body appends
   the four trailers (`Generated-by`, `Comuki-Run`, `Comuki-Mission`
   when present, `Requested-by`) in that order to every commit
   message the agent produces. The trailer values are baked into
   the hook script's literal text at install time — the hook itself
   never re-derives anything at commit time.

The sibling preparer SHALL be a no-op (not an error) on runs without
a source workspace — the same shape as `ProfilesProvider`'s existing
"no profiles source configured → warn and skip" branch. The
`ProfilesProvider.cs` file SHALL NOT be edited by this requirement.

#### Scenario: Hook installed alongside profiles material

- **WHEN** the Translator loop calls `ProfilesProvider.PrepareAsync`
  to land the profiles overlay in a run that also has a source
  workspace
- **THEN** the sibling attribution preparer runs in the same step
  and the resulting workspace contains both the `profiles/` overlay
  and a `prepare-commit-msg` hook in `.git/hooks/` whose body
  references the four trailer values

#### Scenario: No source workspace skips Layer 1

- **WHEN** the run has no source workspace (today's case for every
  run until `feature/source-workspace-clone` / `add-multi-repo-
  projects` merge)
- **THEN** the sibling attribution preparer logs a warning and
  returns without installing a hook or exporting identity env vars,
  matching `ProfilesProvider`'s "warn and skip" shape

#### Scenario: `ProfilesProvider.cs` is not modified

- **WHEN** the sibling attribution preparer is added
- **THEN** `ProfilesProvider.cs` is unchanged — the sibling preparer
  is in a separate file under
  `platform/src/host/Comuki.Host.Translator/Attribution/Hooks/**`

### Requirement: Post-pi verify / fixup pass (Layer 2)

The Translator loop SHALL be extended additively by a verify / fixup
pass that runs between `step 5` ("send the final StageReport") and
`step 7` ("else complete on success or fail with a reason") of the
existing "Translator loop" requirement. The pass SHALL:

1. Read every commit reachable from `HEAD` that is not already on
   the base branch, via `LibGit2Sharp` (Translator already depends
   on that package).
2. For each such commit, assert that author + committer match the
   resolved bot identity and that the commit message carries all
   four trailers in the required order (with `Comuki-Mission`
   permitted-absent when the run has no Mission scope).
3. For each non-conforming commit, run `git commit --amend` to set
   the correct identity and append the four trailers — `amend`
   preserves the tree, only identity / message change.
4. Emit a `run.attribution_applied` journal event on full success
   (with amended-count + trailer values used).
5. Emit a `run.attribution_fixup_partial` journal event when at
   least one commit could not be amended — naming the offending
   commit SHAs. The work item is **not** failed in this case (the
   run still completes; the warning surfaces the gap for review).
6. Be skipped entirely (not an error) when the run has no source
   workspace.

The pass is the actual enforcement — the hook is defense-in-depth.
This matches the "LLM proposes — system disposes" stance in
`openspec/config.yaml` `context:`.

#### Scenario: Verify/fixup amends a misauthored commit

- **WHEN** the agent's final commit set includes a commit whose
  author does not match the resolved bot identity
- **THEN** Layer 2 amends that commit before the work item
  completes, so the recorded commit's author + committer match the
  resolved bot identity and the tree is unchanged

#### Scenario: Verify/fixup appends missing trailers

- **WHEN** the agent's final commit set includes a commit whose
  message body is missing one or more of the four trailers
- **THEN** Layer 2 amends that commit to include the missing
  trailers in the required order, before the work item completes

#### Scenario: Verify/fixup emits the partial-failure event

- **WHEN** Layer 2 cannot amend a non-conforming commit (rare edge
  case — `pre-commit` hook rejecting amend, worktree corruption, etc.)
- **THEN** Layer 2 emits a `run.attribution_fixup_partial` journal
  event naming the offending commit SHA(s) and the work item is
  **not** failed — the run still completes

#### Scenario: Verify/fixup is skipped on a no-source-workspace run

- **WHEN** the run has no source workspace (today's case for every
  run until `feature/source-workspace-clone` / `add-multi-repo-
  projects` merge)
- **THEN** Layer 2 logs a warning and returns without inspecting
  any commits, matching `ProfilesProvider`'s "warn and skip" shape

#### Scenario: Insertion is additive, no existing step removed

- **WHEN** the verify / fixup pass is added to the Translator loop
- **THEN** steps 1–7 of the existing "Translator loop" requirement
  remain present and ordered; the pass is one new step inserted
  between `step 5` and `step 7`, not a replacement of any existing
  step