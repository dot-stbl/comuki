## Purpose

Defines how Comuki workers attribute the commits and pull requests they
produce: the repository's bot identity (R4, per `add-multi-repo-projects`)
as author / committer, four trailers (`Generated-by`, `Comuki-Run`,
`Comuki-Mission`, `Requested-by`) on every commit, and a run-link
footer on every PR the merge queue ingests. Enforced by a two-layer
design Translator owns around the agent's run — a workspace-prep hook
and a post-pi verify / fixup pass — so attribution compliance is
system-enforced, not agent-cooperated.

## ADDED Requirements

### Requirement: Bot-identity commits

Every commit a Comuki worker produces SHALL have its author and
committer set to the repository's bot identity (R4: per-repo credential
/ GitHub App / GitLab integration), resolved through the
`IRepositoryBotIdentity` port. The commit SHALL be attributed as
author = committer = the same identity — no agent-process identity,
no per-developer identity, no system default leaking through. Until R4
ships, the interim `ProjectSettingsAttributionIdentityResolver` (per
workstream 1) supplies the identity from a per-project override or a
module default; when R4 ships, the resolver is replaced wholesale
without changing this requirement's caller surface.

#### Scenario: Commit author is the bot identity

- **WHEN** a worker produces a commit in the source workspace
- **THEN** the commit's `author` and `committer` (name + email) match
  the resolved bot identity byte-for-byte

#### Scenario: Interim identity before R4

- **WHEN** the run is on a Project that has no R4 attachment (because
  `add-multi-repo-projects` has not merged yet) and no
  `ProjectSettings.AttributionIdentityOverride` set
- **THEN** the resolved identity is the module default
  `{ name: "Comuki", email: "noreply@comuki.dev" }`

### Requirement: Four-trailer message contract

Every commit a Comuki worker produces SHALL carry four trailers in its
commit message body, in this exact order: `Generated-by`, `Comuki-
Run`, `Comuki-Mission` (when present, see the next requirement),
`Requested-by`. The trailer block SHALL be preceded by a blank line
and each trailer SHALL match the format `Key: Value`. The four values
are resolved by Translator before it spawns the agent and baked into
the workspace-prep `prepare-commit-msg` hook script's literal text;
the post-pi verify / fixup pass asserts the same values on every
reachable commit.

#### Scenario: All four trailers present in the correct order

- **WHEN** a worker produces a commit in a run with a Mission scope
- **THEN** the commit's message body ends with a blank line followed
  by `Generated-by: …`, then `Comuki-Run: …`, then `Comuki-Mission:
  …`, then `Requested-by: …`

#### Scenario: `Generated-by` value is the Translator's own version

- **WHEN** a commit's `Generated-by` trailer is read
- **THEN** its value is `Comuki v<informationalVersion>` where
  `<informationalVersion>` is the Translator entry assembly's
  `AssemblyInformationalVersionAttribute` value as resolved by
  `ComukiBuildInfo.Read()` — for example
  `Generated-by: Comuki v1.2.3+abc1234` when the build metadata carries
  a sha, or `Generated-by: Comuki v1.2.3` when it does not

#### Scenario: `Comuki-Run` value is the run id from `StageStart`

- **WHEN** a commit's `Comuki-Run` trailer is read
- **THEN** its value equals the `runId` that `StageStart` delivered to
  the Translator before it spawned the agent

### Requirement: `Comuki-Mission` is conditional, never blank

The `Comuki-Mission` trailer SHALL be emitted only when the run belongs
to a Mission (post-Mission-epic, `add-mission-cowork` /
`add-multi-repo-projects` R15's HomeProject / MissionParticipation
split). When the run does not belong to a Mission — either because
the Mission concept is not attached to the run, or because the
Mission epic has not landed yet — the `Comuki-Mission` trailer SHALL
be omitted entirely. The trailer SHALL NEVER be emitted blank,
empty, `unknown`, or with any placeholder value. The verify / fixup
pass SHALL accept "trailer absent" as valid **only** when the run has
no Mission scope.

#### Scenario: Mission run emits the trailer

- **WHEN** a worker produces a commit in a run whose `Run.MissionId`
  is non-null
- **THEN** the commit's message body contains a `Comuki-Mission:
  <mission-id>` trailer

#### Scenario: Non-Mission run omits the trailer

- **WHEN** a worker produces a commit in a run whose `Run.MissionId`
  is null (today's case — the Mission epic has not landed)
- **THEN** the commit's message body does **not** contain a
  `Comuki-Mission` trailer (no blank `Comuki-Mission:` line, no
  `Comuki-Mission: unknown`, no `Comuki-Mission: `)

### Requirement: `Requested-by` resolution table

The `Requested-by` trailer SHALL be resolved per the run's origination
path:

- **Intake-originated runs** resolve to
  `IncomingTicket.Author` (the source tracker's login / display name,
  verbatim from `platform/src/modules/Intake/Comuki.Modules.Intake.
  Domain/Tickets/IncomingTicket.cs:40`).
- **Chat / dashboard-originated runs** resolve to the authenticated
  subject — the same shape
  `RequiresPermissionFilter.cs:57` uses for `context.HttpContext.User`
  — typically `ClaimsPrincipal.Identity?.Name` or a configured
  display-name claim.
- **Scheduled-job-originated runs** (the
  `IScheduledRunLauncher` brief shape `{ goal, scheduleId,
  scheduleSlug, firedAt }`) resolve to the fixed literal
  `comuki-scheduler`. This is a system identity, not a human name,
  and never pretends to be one.

The `Requested-by:` trailer is deliberately NOT spelled
`Co-Authored-By:` because the no-AI-attribution gate's
`WHOLE_LINE_PATTERNS` includes `^co-authored-by:.*?\b<vendor>`. Using
a distinct name is a real design choice, not a naming accident — see
`design.md` D4.

#### Scenario: Intake-originated run carries the tracker author

- **WHEN** an intake webhook admits a ticket and the resulting run
  produces a commit
- **THEN** the commit's `Requested-by` trailer value equals the
  tracker login / display name verbatim, with no Comuki-side
  rewriting

#### Scenario: Scheduled-job run carries the system identity

- **WHEN** a scheduled job (`IScheduledRunLauncher`-shaped brief)
  fires and the resulting run produces a commit
- **THEN** the commit's `Requested-by` trailer value is the literal
  string `comuki-scheduler`

### Requirement: Two-layer enforcement (hook + verify/fixup)

Attribution SHALL be enforced in two layers, both Translator-owned:

- **Layer 1 — workspace preparation (before the agent spawns).**
  Translator SHALL set the source workspace's git identity via
  `GIT_AUTHOR_NAME` / `GIT_AUTHOR_EMAIL` / `GIT_COMMITTER_NAME` /
  `GIT_COMMITTER_EMAIL` env vars exported into the `pi` child process,
  AND SHALL install a `prepare-commit-msg` hook at
  `<workspace>/.git/hooks/prepare-commit-msg` whose body appends the
  four trailers to every commit message the agent produces. Both
  operations are sibling preparers to the existing `ProfilesProvider`,
  not modifications of it.
- **Layer 2 — verify / fixup (after the agent exits, before the
  work item completes).** Translator SHALL inspect every commit
  reachable from `HEAD` that is not already on the base branch; for
  each, assert that author + committer match the resolved bot
  identity and that the message carries the required trailers; amend
  non-conforming commits via `git commit --amend` (preserves the
  tree, only identity / message change) to the correct state.

The verify / fixup pass is the actual guarantee — the hook is
defense-in-depth. This matches the "LLM proposes — system disposes"
line in `openspec/config.yaml` `context:`. Layer 1 alone depends on
agent cooperation (`git commit --no-verify` bypasses the hook);
Layer 2 alone works but wasteful; both together are required.

#### Scenario: Hook appends the four trailers

- **WHEN** the agent runs `git commit -m "fix: handle edge case"` in
  the source workspace
- **THEN** the recorded commit's message body ends with the four
  trailers in the required order, because the
  `prepare-commit-msg` hook appended them

#### Scenario: Verify/fixup amends an agent that bypassed the hook

- **WHEN** the agent runs `git commit --no-verify -m "fix: …"` (or
  otherwise produces a commit the hook did not see) and the
  resulting commit has the wrong identity or missing trailers
- **THEN** Layer 2 amends the commit before the work item completes,
  so the recorded commit's author + committer + trailers match the
  contract

#### Scenario: Verify/fixup does not fail the work item on amend failure

- **WHEN** Layer 2 cannot amend a non-conforming commit (the rare
  edge case — `pre-commit` hook rejecting amend, worktree corruption,
  etc.)
- **THEN** the failure is logged with the offending commit SHA and
  a `run.attribution_fixup_partial` journal event is emitted, but
  the work item is **not** failed — the run still completes; the
  warning surfaces the gap for review and a follow-up clean fixup or
  human amend can resolve the residue

#### Scenario: Verify/fixup is skipped when no source workspace

- **WHEN** the run has no source workspace (today's case for any run
  on a Project without `SourceGitUrl` / `SourceGitRef`, and
  certainly every run until `feature/source-workspace-clone` /
  `add-multi-repo-projects` merge)
- **THEN** Layer 2 is skipped entirely, not an error — same shape as
  `ProfilesProvider`'s existing "no profiles source configured →
  warn and skip" branch

### Requirement: PR description footer (PATCH-after-create)

Every PR whose URL Comuki ingests via the merge queue's
`PullRequestUrls` ingestion point SHALL have a Comuki footer appended
to its description body via `IPullRequestAnnotator.EnsureFooterAsync`,
even when the PR was created by the agent or a human rather than by
Comuki. The footer is appended via the git host's PATCH-description
API (e.g. `PATCH .../repos/{owner}/{repo}/pulls/{number}` for GitHub)
using the R4 (or interim) credential resolved at PATCH-call time, not
at workspace-prep time. A 2xx response is success; a 422 (PR already
has the footer) is success; a 404 / 410 is logged with a journal
warning, not an error.

#### Scenario: Footer appended to a freshly-created PR

- **WHEN** the merge queue ingests a PR URL whose description body
  does not yet contain the Comuki footer
- **THEN** `IPullRequestAnnotator.EnsureFooterAsync` issues a PATCH
  that appends the footer and the PR's body now contains it

#### Scenario: Footer not re-appended when already present

- **WHEN** the merge queue ingests a PR URL whose description body
  already contains the Comuki footer
- **THEN** `IPullRequestAnnotator.EnsureFooterAsync` does not issue
  a PATCH that mutates the body

#### Scenario: PR footer link uses the configured public URL

- **WHEN** the footer is appended to a PR
- **THEN** the footer link's host is `AuthPublicHostOptions.PublicUrl`
  (the existing `COMUKI_PUBLIC_HOST_URL` setting — operator-
  configured, never the inbound `Host` header), and the link path is
  `/projects/<projectId>/runs/<runId>` for the run that the PR
  represents

### Requirement: White-label toggle is edition-gated

A repository policy SHALL expose `AllowHiddenAttribution: bool` (the
field lives on `RepositoryPolicy`, R3-owned, in the `repositories`
capability owned by `add-multi-repo-projects`). The field SHALL be
defaulted to `false`. When the field is `true` AND the resolving
edition covers the `Features.CommitAttributionWhiteLabel` feature
key (the `Comuki.Shared.Editions.Features` registry, owned by the
sibling change `add-editions-and-licensing`), the trailers SHALL be
replaced per the repository's `AttributionReplacement` policy.
Community edition: the field SHALL be immutable at `false`; an
endpoint attempting to set it SHALL return
`edition.feature_unavailable`. Attribution SHALL be **always** on
in community edition; the toggle is unavailable, not optional.

#### Scenario: Community edition refuses to enable the toggle

- **WHEN** an operator on a community-edition Comuki attempts to set
  `AllowHiddenAttribution: true` on a repository's policy
- **THEN** the endpoint returns `edition.feature_unavailable` and the
  field remains `false`

#### Scenario: Paid edition with toggle enabled replaces trailers

- **WHEN** a repository has `AllowHiddenAttribution: true` and the
  resolving edition covers
  `Features.CommitAttributionWhiteLabel`
- **THEN** Layer 2's verify / fixup pass accepts the
  `AttributionReplacement` template as a valid alternative to the
  four trailers and the PR footer is suppressed or replaced per the
  template

### Requirement: Coexistence with the no-AI-attribution gate

The four Comuki trailers and the PR footer SHALL coexist with this
repo's no-AI-attribution gate (`scripts/commit-lint.mjs` +
`.agents/rules/process/no-ai-attribution.md`) without any change to
the gate's `AI_VENDORS` / `WHOLE_LINE_PATTERNS` / `INLINE_PATTERNS`.
This is verified by direct inspection: `AI_VENDORS` does not contain
`"comuki"` (the list is closed and vendor-shaped); none of the four
Comuki trailers or the PR footer matches any `WHOLE_LINE_PATTERNS` or
`INLINE_PATTERNS` entry. The `Requested-by:` trailer is deliberately
distinct from `Co-Authored-By:` for the same reason — using
`Co-Authored-By:` would risk false-positive trips on the gate.

#### Scenario: All four trailers survive the commit-lint hook

- **WHEN** a commit message containing the four trailers in order
  reaches the commit-msg hook
- **THEN** `stripAttribution` leaves the message byte-for-byte
  unchanged (`text === original`, `removed === []`)

#### Scenario: PR footer survives the commit-lint hook

- **WHEN** a commit message containing the PR footer verbatim reaches
  the commit-msg hook
- **THEN** `stripAttribution` leaves the message byte-for-byte
  unchanged

### Requirement: Pinned regression test for the non-collision guarantee

A regression test SHALL live in `scripts/commit-lint.test.mjs`
asserting that `stripAttribution` leaves a commit message containing
all four Comuki trailers and the PR footer byte-for-byte unchanged.
The test SHALL fail loudly if a future edit to `AI_VENDORS` /
`WHOLE_LINE_PATTERNS` / `INLINE_PATTERNS` in `commit-lint.mjs`
(e.g. someone reflexively adding `"comuki"` thinking it is a vendor)
breaks the non-collision guarantee.

#### Scenario: Refusing to add "comuki" to `AI_VENDORS`

- **WHEN** a hypothetical future commit adds the literal string
  `"comuki"` to `AI_VENDORS` in `scripts/commit-lint.mjs`
- **THEN** the pinned regression test fails because
  `stripAttribution` now strips the `Requested-by: …` line (no, it
  doesn't — `Requested-by:` is not `co-authored-by:` — but the test
  asserts the wider invariant: the four trailers and the PR footer
  survive the gate regardless of any future `AI_VENDORS` /
  pattern edits, and any edit that breaks this fails the test)