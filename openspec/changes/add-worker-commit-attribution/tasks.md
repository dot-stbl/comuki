Workstreams are sized for independent MiniMax/opencode agents on disjoint
file areas — each lists the exact projects/paths it owns so two
workstreams never edit the same file. Deps are hard prerequisites
(must merge first); gates are the commands each workstream's own agent
runs before handing back. The orchestrator still runs
`dotnet build comuki.slnx -c Debug` and the full test suites across
workstreams before archiving this change.

This change is mostly greenfield on the source-workspace side — Layer 1
(Layer 1 workspace-prep hook) and Layer 2 (verify/fixup pass) are
specified against the **future shape** of `add-multi-repo-projects`
(#163) and `feature/source-workspace-clone` (#125), neither of which
has merged to `master` as of this branch. The implementing workstreams
stub against the interim `IRepositoryBotIdentity` resolver (D11) and
gate their integration tests on `dotnet ef` migrations existing in the
Repo module. See `design.md` "Verified state" for the full
non-existence list.

The `editions` capability is owned by the sibling change
`add-editions-and-licensing` (#164) authored in parallel this session;
workstream 5 explicitly depends on that change landing.

## 1. `IRepositoryBotIdentity` port + interim resolver

- [ ] 1.1 Define `IRepositoryBotIdentity` port: one method
  `BotIdentity Resolve(RepositoryId repositoryId, ProjectId projectId,
  CancellationToken ct)`, returning a `BotIdentity(Name, Email)` value
  type. Lives in
  `platform/src/host/Comuki.Host.Translator/Attribution/**` (new
  folder). No reference to `Comuki.Modules.Repositories` implementation
  types — mirrors how Orchestration references `ProjectId` by value
  today; port is host-side and resolves through DI only.
- [ ] 1.2 Implement `ProjectSettingsAttributionIdentityResolver`: reads
  `Project.Settings.AttributionIdentityOverride?: string` (new field on
  `Comuki.Modules.Projects.Domain.ProjectSettings`, additive — does
  not break existing settings shape) and resolves to
  `BotIdentity.Parse(override)`; falls back to
  `ComukiOptions.CommitIdentity` default
  (`{ name: "Comuki", email: "noreply@comuki.dev" }`). Verifies that
  the module default is set on Translator boot
  (`ValidateOnStart`); missing default is a build failure, not a
  runtime fallback.
- [ ] 1.3 Add `ComukiOptions.CommitIdentity` to
  `platform/src/host/Comuki.Host.Translator/Configuration/`, default
  `{ name: "Comuki", email: "noreply@comuki.dev" }`, env-var bind
  `COMUKI_COMMIT_IDENTITY_NAME` / `COMUKI_COMMIT_IDENTITY_EMAIL`,
  `ValidateDataAnnotations().ValidateOnStart()`.
- [ ] 1.4 Add `ProjectSettings.AttributionIdentityOverride?: string` to
  `Comuki.Modules.Projects.Domain.ProjectSettings`; document the
  interim nature (replaced wholesale by R4 when `add-multi-repo-
  projects` lands).
- [ ] 1.5 Unit tests: `ProjectSettingsAttributionIdentityResolver`
  resolves override > default > fails on missing default; `BotIdentity.
  Parse` rejects malformed strings (no `@`).

Deps: none. Files:
`platform/src/host/Comuki.Host.Translator/Attribution/BotIdentity.cs`
(new), `Attribution/IRepositoryBotIdentity.cs` (new),
`Attribution/ProjectSettingsAttributionIdentityResolver.cs` (new),
`Configuration/ComukiOptions.cs` (extend, not replace),
`platform/src/modules/Projects/Comuki.Modules.Projects.Domain/
ProjectSettings.cs` (additive field), `platform/src/modules/Projects/
Comuki.Modules.Projects.Infrastructure/Migrations/` (new `dotnet ef`
migration for the additive `attribution_identity_override` column —
never hand-edit per `ef-migrations.md`).
Gates: `dotnet run --project tests/unit/Comuki.Host.Translator.Unit`
(new project, this workstream creates it).

## 2. Trailer resolution + `prepare-commit-msg` hook writer (Layer 1)

- [ ] 2.1 Implement `CommitTrailerResolver` (sibling to
  `ProfilesProvider`, not inside it): resolves the four trailer values
  per D7 and D8. Reads
  `ComukiBuildInfo.Read(Assembly.GetEntryAssembly())` for the version;
  reads `runId` from the `StageStart` brief; reads `RequestedBy` from
  the work-item brief (which workstream 1's plumbing populates); reads
  `MissionId` from `Run.MissionId` when present (post-Mission-epic —
  today the field does not exist, so the resolver returns null and the
  trailer is omitted).
- [ ] 2.2 Implement `PrepareCommitMsgHookWriter` (Layer 1's hook
  writer): writes a POSIX `prepare-commit-msg` shell script to
  `<workspace>/.git/hooks/prepare-commit-msg` (chmod 755 on POSIX, no
  Windows execute bit needed — git invokes it through the shell on
  Windows too). The script reads `COMMIT_EDITMSG` (the file path git
  passes as `$1`), checks that no `Generated-by:` / `Comuki-Run:` /
  `Comuki-Mission:` / `Requested-by:` line is already present (avoid
  duplication on amend), then appends a blank line + the four
  trailers in D6 order.
- [ ] 2.3 Hook script template is a `file static class
  PrepareCommitMsgHookTemplate` (NOT a generated file — a hand-written
  literal script stored as a C# verbatim string). Tests assert the
  generated script's literal text against a pinned fixture, so an
  accidental edit to the template is caught.
- [ ] 2.4 Implement `IdentityEnvExporter` (Layer 1's environment
  variable setter): builds the four `GIT_AUTHOR_*` / `GIT_COMMITTER_*`
  env vars from the resolved `BotIdentity` and returns them as a
  dictionary to be merged into the `pi` child-process environment.
  Pure function, easy to unit-test.
- [ ] 2.5 Wire both into the Translator loop as a sibling preparer
  alongside `ProfilesProvider` (NOT inside it — D14). The Translator
  loop's step 2 calls `ProfilesProvider.PrepareAsync`, then
  `AttributionPreparer.PrepareAsync` — same shape, separate code path,
  separate file. `ProfilesProvider.cs` is not edited.
- [ ] 2.6 Unit tests: `CommitTrailerResolver` returns the four trailers
  in D6 order, omits `Comuki-Mission` when `Run.MissionId` is null,
  formats `Generated-by: Comuki v<informationalVersion>` (with `+sha`
  when the build metadata carries one); `PrepareCommitMsgHookWriter`
  produces a script that survives `bash -n` syntax check and matches
  the pinned fixture; `IdentityEnvExporter` produces the four env
  vars with the resolved name / email; the hook writer's existing-
  trailer guard prevents duplication on amend.

Deps: 1 (needs `IRepositoryBotIdentity`). Files:
`platform/src/host/Comuki.Host.Translator/Attribution/CommitTrailerResolver.cs`
(new), `Attribution/PrepareCommitMsgHookWriter.cs` (new),
`Attribution/PrepareCommitMsgHookTemplate.cs` (new — the literal
script), `Attribution/IdentityEnvExporter.cs` (new),
`Attribution/AttributionPreparer.cs` (new — the sibling preparer
that the Translator loop calls),
`platform/src/host/Comuki.Host.Translator/TranslationLoop.cs` (extend
step 2 only — append `AttributionPreparer.PrepareAsync` call; the
rest of the loop is not touched per D14 and D15).
Gates: `dotnet run --project tests/unit/Comuki.Host.Translator.Unit`
(the same project workstream 1 creates; extend with the new test
fixtures).

## 3. Verify/fixup pass (Layer 2)

- [ ] 3.1 Implement `AttributionVerifier` (Layer 2's verifier): reads
  commits reachable from `HEAD` that are not already on the base branch
  via `LibGit2Sharp` (Translator already depends on this package),
  asserts author + committer match the resolved `BotIdentity`, asserts
  the commit message contains all four required trailers (in D6
  order, with `Comuki-Mission` permitted-absent when the run has no
  Mission scope).
- [ ] 3.2 Implement `AttributionFixer` (Layer 2's amender): for each
  non-conforming commit, runs `git commit --amend --author="<Name>
  <Email>" -m "<message with trailers appended>"` (preserves the tree,
  D2). The amend command's `GIT_AUTHOR_*` / `GIT_COMMITTER_*` env
  vars are set from the resolved `BotIdentity` so the amend itself
  is identity-clean. Failure to amend emits a `run.attribution_
  fixup_partial` journal event naming the offending commit SHA but
  does not fail the work item (D10).
- [ ] 3.3 Implement `AttributionJournalEmitter`: emits
  `run.attribution_applied` (success case, with amended-count +
  trailer values used) and `run.attribution_fixup_partial` (partial
  case, with offending SHAs) via the existing
  `Comuki.Engine.Orchestration.Journaling.RunJournal` API (cite the
  exact API surface at implementation time — read
  `Comuki.Engine.Orchestration/Journaling/` to confirm the emit
  signature).
- [ ] 3.4 Wire into the Translator loop between step 5 ("send the
  final StageReport") and step 7 ("else complete on success or fail
  with a reason") per D15. The insertion is additive — no step is
  removed. Runs only when a source workspace exists for the run (D9);
  skipped otherwise, not an error.
- [ ] 3.5 OTel metrics: `comuki.attribution.commits.amended` (counter)
  and `comuki.attribution.commits.verified_clean` (counter), registered
  via the existing `Comuki.Host.Translator/Attribution/**` Meter per
  `observability/diagnostics.md` §3 (`*.comuki.*` lowercase dot.case).
- [ ] 3.6 Unit tests: `AttributionVerifier` correctly classifies
  conforming commits as clean; `AttributionFixer` produces an amend
  command whose message contains all four trailers in D6 order;
  Layer 2 emit logic emits `run.attribution_applied` on full success,
  `run.attribution_fixup_partial` when at least one commit could not
  be amended, and **does not** fail the work item in the partial case
  (D10).

Deps: 1, 2. Files:
`platform/src/host/Comuki.Host.Translator/Attribution/AttributionVerifier.cs`
(new), `Attribution/AttributionFixer.cs` (new),
`Attribution/AttributionJournalEmitter.cs` (new),
`Attribution/AttributionMetrics.cs` (new — Meter + Counter
declarations), `platform/src/host/Comuki.Host.Translator/TranslationLoop.cs`
(extend to insert Layer 2 between steps 5 and 7; the rest of the
loop is not touched).
Gates: `dotnet run --project tests/unit/Comuki.Host.Translator.Unit`
(extend with fake-git-repo fixtures — see workstream 6 for the
shared fixture base).

## 4. PR footer `PATCH-after-create` (`IPullRequestAnnotator`)

- [ ] 4.1 Define `IPullRequestAnnotator` port: one method
  `Task EnsureFooterAsync(PullRequestUrl url, RunFooter footer,
  CancellationToken ct)`. The port is host-side (D3), not Translator-
  side. Lives in `platform/src/host/Comuki.Host/Attribution/**` (new
  folder).
- [ ] 4.2 Define `PullRequestUrl` (string-typed newtype for clarity)
  and `RunFooter` (record carrying `RunId`, `ProjectId`,
  `PublicUrl` — the same `AuthPublicHostOptions.PublicUrl` resolved
  per `AuthPublicHostOptions.cs:34`, reused not re-invented).
- [ ] 4.3 Define `IGitHubPullsClient` Refit interface:
  `[Patch("/repos/{owner}/{repo}/pulls/{number}")] Task<Unit>
  UpdatePullRequestAsync(string owner, string repo, int number,
  [Body] PullRequestUpdate body, CancellationToken ct);` plus a
  `PullRequestUpdate` record carrying the merged body. Lives in
  `platform/src/host/Comuki.Host/Attribution/GitHub/IGitHubPullsClient.cs`
  (new). Per D12: Refit (not Octokit), reuses Intake's auth shape.
- [ ] 4.4 Implement `GitHubPullRequestAnnotator`: reads
  `GitHubSettings` from the repository's settings jsonb (the same
  shape `platform/src/modules/Intake/Comuki.Modules.Intake.
  Infrastructure/Providers/GitHub/GitHubSettings.cs` already parses,
  by reference — do not move or duplicate the Intake file). Resolves
  the bearer token from `GitHubSettings.ApiTokenEnv` via
  `IConfiguration` at call time (D5's Open Question 5 — not the
  workspace-prep-time credential). Calls the Refit client. Success on
  2xx and 422 (PR already has the footer; the gate fires only when
  the body is unchanged in conflict); 404 / 410 logged + journal
  warning, not an error.
- [ ] 4.5 Wire into the merge-queue ingestion path: when
  `MergeQueueEntry.PullRequestUrls` gains a new URL (or, during the
  migration window, when the existing per-batch list is populated),
  the Host's merge-queue ingestion handler invokes
  `IPullRequestAnnotator.EnsureFooterAsync` for each new URL with the
  relevant `RunFooter` resolved from `AuthPublicHostOptions`. The
  wiring is additive — does not modify `MergeQueueEntry` /
  `MergeBatch` shape.
- [ ] 4.6 Unit tests: `GitHubPullRequestAnnotator` issues a PATCH with
  the footer-appended body (test against a `WireMockServer` or
  in-test fake `HttpMessageHandler`); 422 path does not retry; 404 /
  410 path logs but does not throw.

Deps: 1 (for `BotIdentity`, used in the `Requested-by` portion of the
footer). Files: `platform/src/host/Comuki.Host/Attribution/IPullRequestAnnotator.cs`
(new), `Attribution/PullRequestUrl.cs` (new),
`Attribution/RunFooter.cs` (new), `Attribution/GitHub/IGitHubPullsClient.cs`
(new), `Attribution/GitHub/PullRequestUpdate.cs` (new),
`Attribution/GitHub/GitHubPullRequestAnnotator.cs` (new),
`platform/src/host/Comuki.Host/MergeQueue/Ingestion/` (extend to
invoke the annotator — additive handler, no schema change).
Gates: full `dotnet build comuki.slnx -c Debug` (this workstream
spans the Host's merge-queue ingestion path).

## 5. Edition gate integration for the white-label toggle

- [ ] 5.1 Add `RepositoryPolicy.AllowHiddenAttribution: bool` (default
  `false`) to the `repositories` capability's `RepositoryPolicy`
  aggregate. This is a **schema change** on the not-yet-landed
  Repositories module — coordinate with the `add-multi-repo-projects`
  workstream 2 (RepositoryPolicy implementation) to land this field
  there, not in this change.
- [ ] 5.2 Add `RepositoryPolicy.AttributionReplacement?:
  AttributionReplacementKind` (enum: `None`, `ReplaceTemplate`)
  alongside 5.1. The `ReplaceTemplate` case carries a string template
  field (placeholder grammar is implementation-defined at workstream
  time). Same coordination note as 5.1.
- [ ] 5.3 Define `Features.CommitAttributionWhiteLabel` as a feature
  key in `Comuki.Shared.Editions.Features` (the registry type owned
  by `add-editions-and-licensing`). Reference by name only — the
  registry's full API is not invented here.
- [ ] 5.4 Wrap the white-label toggle's read path (5.1's
  `AllowHiddenAttribution`) with `[RequiresFeature(Features.
  CommitAttributionWhiteLabel)]` per the editions gate shape.
  Community edition: the policy field is immutable at `false`; an
  endpoint attempting to set it returns `edition.feature_unavailable`.
- [ ] 5.5 When the white-label toggle is set AND a paid edition covers
  it, Layer 2's verify/fixup pass accepts the
  `AttributionReplacement` template as a valid alternative to the four
  trailers (Layer 1's hook writer still emits the trailers verbatim;
  Layer 2 strips / replaces them per the policy). Test: a fake repo
  with `AllowHiddenAttribution: true` + a paid-edition-enabled
  Project sees the trailer set replaced per the template.

Deps: `add-multi-repo-projects` (lands `RepositoryPolicy` aggregate
this change extends), `add-editions-and-licensing` (owns `Features.*`
registry + `[RequiresFeature]`/`IEdition` gate shape). Files:
coordination-only — no new files in this workstream until both deps
merge. After they merge: `platform/src/modules/Repositories/
Comuki.Modules.Repositories.Domain/RepositoryPolicy.cs` (extend
additively), `platform/src/host/Comuki.Host.Translator/Attribution/
` (extend Layer 1 + Layer 2 to honor the toggle).
Gates: full `dotnet build comuki.slnx -c Debug` after both deps land.

## 6. Tests + docs

- [ ] 6.1 Pinned regression test in `scripts/commit-lint.test.mjs`:
  add a new `describe('comuki trailers — allowlisted by being distinct
  from AI vendor patterns', ...)` block with at least these cases:
  (a) `stripAttribution` leaves a commit message containing all four
  trailers byte-for-byte unchanged (`text === original`, `removed
  === []`); (b) the same for a message containing the PR footer
  verbatim; (c) `lintSubject` accepts the same messages. Per D13:
  no changes to `AI_VENDORS` / `WHOLE_LINE_PATTERNS` /
  `INLINE_PATTERNS` in `commit-lint.mjs`. Run via
  `node --test scripts/commit-lint.test.mjs` (the existing test
  runner). Verify the existing 527-line test suite still passes.
- [ ] 6.2 T1 integration (Testcontainers Postgres): a unit suite
  `tests/unit/Comuki.Host.Translator.Unit` (created in workstream 1)
  with a fake-git-repo fixture base (a real local `git init` repo
  inside the test's working directory, with `LIBGIT2SHARP_TEST_REPO`
  pattern if one exists already — read `tests/tools/` for any
  pre-existing fake-git-repo fixtures before creating new ones; if
  none, create a shared `FakeGitRepo` builder under
  `tests/unit/Comuki.Host.Translator.Unit/Fixtures/`). Cover: Layer 1
  hook script survives `bash -n` syntax check + the hook actually
  appends trailers when invoked through a real `git commit`; Layer 2
  verifier flags a misauthored commit; Layer 2 fixer amends it; full
  verify-then-fixup loop on a 3-commit fixture with one bad commit
  emits `run.attribution_applied` and leaves one amended commit +
  two clean commits.
- [ ] 6.3 **T2 scenario** per `openspec/changes/add-agentic-test-
  contour/specs/agentic-testing/spec.md`'s "T2a proves container
  lifecycle without a model" requirement: a
  `tests/tools/Comuki.AgentTest.Runner/scenarios/attribution/
  end-to-end.yaml` scenario in `fake` mode — runs a fake-mode agent
  loop that ends with a produced git commit, then asserts on the
  resulting repository: (a) author and committer match the resolved
  `BotIdentity`; (b) the commit message contains all four trailers in
  D6 order; (c) when the run has no Mission scope, `Comuki-Mission`
  is correctly omitted (not emitted as `Comuki-Mission:` or
  `Comuki-Mission: unknown`); (d) when a `comuki-scheduler`-origin
  fixture is used, `Requested-by: comuki-scheduler` appears
  verbatim. This is the exact T2 the parent task asked for by name
  (issue #165's "configured in the repository policy" requires a
  fake-mode end-to-end pass, not unit-only).
- [ ] 6.4 Architecture test: `Comuki.Host.Translator` does not
  reference `Comuki.Modules.Repositories` **implementation** types
  (only `RepositoryId` by value, mirroring how Orchestration references
  `ProjectId` today). Verify `Comuki.Architecture.Tests` stays green.
- [ ] 6.5 Documentation: extend
  `.agents/docs/architecture/worker-runtime.md` (if it exists, or
  create the page if not) with a short section on the attribution
  two-layer pattern; mention the slice in `.agents/STATE.md` /
  `.agents/ROADMAP.md`. Reference `openspec/changes/add-worker-
  commit-attribution/` from those files.

Deps: 1, 2, 3, 4, 5. Gates: full
`dotnet build comuki.slnx -c Debug` (warnings-as-errors + format) +
full unit suite + `node --test scripts/commit-lint.test.mjs` (the
new regression test included) + T1 (Testcontainers) + T2
(`bun run tools:agent-test -- scenario attribution/end-to-end.yaml
--mode fake`). T3 is not required for this change — no real git-host
fixture is part of the contract; the T2 fake-mode pass is the E2E
floor.

## Wave slot notes

- The orchestrator running all six workstreams should land workstreams
  1, 2, 3, 4 in parallel (independent file areas: `Attribution/Bot
  Identity*`, `Attribution/Hooks*`, `Attribution/Verification*`, Host
  `Attribution/`). Workstream 5 (`RepositoryPolicy` + `Features.*` +
  edition gate) depends on `add-multi-repo-projects` AND
  `add-editions-and-licensing` landing first — gate its start on
  those two PRs merging.
- Workstream 6 (tests + docs) gates on 1–5.
- Until `feature/source-workspace-clone` (#125) and
  `add-multi-repo-projects` (#163) merge, the T2 scenario's "fake-mode
  agent loop ends with a produced git commit" step needs an interim
  stand-in — a fake-mode agent loop that operates on a temporary
  `git init` repo the test creates, not on a real product repository
  clone. The test asserts on that temporary repo's final state.
  Document this interim shape in the test's preamble so future work
  knows to swap to a real product-repo fixture once #125 / #163 merge.