## Context

Issue #165 binds the requirement: commits/PRs produced by Comuki workers
use the repository's bot identity (per-repo credential / GitHub App, R4)
as author/committer and carry trailers `Generated-by: Comuki vX.Y.Z`,
`Comuki-Run: <run-id>`, `Comuki-Mission: <mission-id>`, `Requested-by:
<human>`; PR description footer with a run link. Configured in the
repository policy (R3); cannot be disabled in community edition, can be
hidden/customized (white-label) in paid — depends on
`add-editions-and-licensing` (#164). Must coexist with the no-AI-attribution
gate (`scripts/commit-lint.mjs` + `.agents/rules/process/no-ai-attribution.md`)
for this repo — Comuki trailers are allowlisted by being naturally distinct
from the patterns the gate trips on.

This design specifies against a verified state of `platform/src` on this
branch, dated 2026-09-25.

### Verified state — the change is mostly greenfield

There is **no git-commit or PR-creation code in `platform/src` today**.
Verified by direct search before this design was written:

- The only `LibGit2Sharp` usage anywhere in `platform/src` is
  `platform/src/host/Comuki.Host.Translator/Profiles/ProfilesProvider.cs`
  — a **read-only** shallow clone + checkout of the *profiles* overlay
  (agent-brief material), not the product source repository. Translator
  already depends on `LibGit2Sharp` for this purpose, so any
  commit-inspection code this change adds is on a dependency it already
  takes.
- `SourceGitUrl`, `ISourceWorkspacePreparer`, `ClaimWorkspace` — the
  product-source clone concept that `add-multi-repo-projects/design.md`
  describes as "today" — **do not exist in this worktree's `platform/src`**
  (verified by grep: zero hits). `feature/source-workspace-clone`
  (issue #125), which `add-multi-repo-projects/design.md` treats as a
  prerequisite already landed, **has not actually landed on `master`**
  as of this branch. There is currently no code path that clones a
  writable product repository into a worker's workspace at all.
- No PR-creation API call exists anywhere in `platform/src`. The merge
  queue (`Comuki.Engine.Orchestration.Domain.MergeQueue.MergeQueueEntry`
  / `MergeBatch`) stores **operator-declared PR URLs** (a human or the
  agent supplies the URL string after creating the PR themselves) —
  Comuki does not call `POST /repos/.../pulls` anywhere today.
- The current `openspec/specs/worker-runtime/spec.md`'s "Translator
  loop" requirement is the **landed** contract. Its 7-step loop
  (claim → prepare profiles → open grpc → spawn agent + pump stream →
  report → lease-check → complete/fail) has no step for git identity,
  commit trailers, or PR handling. The `pi` coding agent is an
  autonomous CLI that shells out to the **system `git` binary** itself
  — Translator does not intercept or wrap individual git invocations
  the agent makes; it only prepares the workspace *before* pi starts
  and inspects the result *after* pi's process exits (see "Agent
  invocation and stream parsing" in the same spec).
- `platform/src/engine/Comuki.Engine.Orchestration/Domain/Runs/Run.cs`
  has no `MissionId` field today (Mission is a separate, not-yet-landed
  epic — `add-mission-cowork` / `add-multi-repo-projects` R15's
  HomeProject/MissionParticipation split). It also has no `RequestedBy`
  field — see "Open Question 3" below.
- The Translator already has `runId` from `StageStart` (worker-runtime
  spec: "`Start` (StageStart: workItemId, runId, brief)") before it
  spawns pi.
- `platform/src/modules/Intake/Comuki.Modules.Intake.Domain/Tickets/
  IncomingTicket.cs:40` carries `Author` ("Author login / display name
  on the source tracker") — the intake-originated resolution source.
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/
  Security/Authorization/RequiresPermissionFilter.cs:57` reads
  `context.HttpContext.User` to resolve the authenticated subject — the
  shape chat/dashboard-originated runs would mirror.
- `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/
  Providers/GitHub/GitHubSettings.cs` already defines
  `(Owner, Repo, ApiBase, ApiTokenEnv, IncludePullRequests)` — the
  nearest existing shape for a git-host API credential/settings record.
  Intake's is read-only (fetches issues); the PR-annotation write side
  (`PATCH .../pulls/{number}`) is new, reusing the same settings shape.
- `platform/src/shared/Comuki.Shared.Bootstrap/Versioning/
  ComukiBuildInfo.cs:34` reads
  `AssemblyInformationalVersionAttribute` from any assembly — the
  Translator binary is built from this same monorepo at the same commit
  as everything else (worker-runtime spec's "Worker container image"
  requirement — multi-stage build, SDK stage compiles+publishes the
  Translator), so it can read its **own** assembly's informational
  version at runtime with zero new plumbing.
- `platform/src/host/Comuki.Host/Auth/AuthPublicHostOptions.cs:34` is
  the existing `PublicUrl` option (`COMUKI_PUBLIC_HOST_URL`), the
  existing "operator-configured public base URL, never the inbound Host
  header" pattern — same rationale applies to a customer-facing PR
  footer link (must not be built from an attacker-controlled header).
- `scripts/commit-lint.mjs` line 98: `AI_VENDORS = ["claude",
  "anthropic", "chatgpt", "openai", "gpt-", "codex", "copilot",
  "cursor", "gemini", "opencode", "devin", "aider", "windsurf"]` —
  `"comuki"` is not on the list. None of the four Comuki trailers or
  the PR footer trip any existing pattern (verified by inspection:
  `Generated-by: Comuki vX.Y.Z` does not match `^(?:🤖\s*)?generated
  with\b` — different words, "Generated-by" vs "generated with";
  `Comuki-Run:` / `Comuki-Mission:` / `Requested-by:` do not match any
  `co-authored-by:` / `assisted-by:` / `ai-*` pattern; no
  `noreply@anthropic.com` or vendor domain is involved). **No change
  to `AI_VENDORS` / `WHOLE_LINE_PATTERNS` / `INLINE_PATTERNS` in
  `scripts/commit-lint.mjs` is required for these exact trailer names
  to survive the hook as-is.**

### Why this design has two layers

Because `pi` shells out to the system `git` binary (not `LibGit2Sharp`,
and not through Translator-mediated calls), Comuki cannot intercept
each individual `git commit` the agent runs. A single-layer design
("just ask the agent nicely in the brief") is insufficient: it puts
attribution compliance on agent cooperation, not on the system. The
`openspec/config.yaml` `context:` block states "Brain thinks;
deterministic code is the spine ('LLM proposes — system disposes')" —
this design implements that stance for git identity: the hook is
defense-in-depth, the verify/fixup pass is the guarantee.

## Goals / Non-Goals

**Goals:**

- Make every commit a Comuki worker produces carry the repository's bot
  identity and the four trailers, deterministically, without relying on
  the agent to remember to do it.
- Stamp a PR-description footer containing a run link on every PR the
  merge queue ingests, deterministically, without relying on the agent
  to compose it.
- Build the contract in two deterministic layers around the agent's
  run (Translator-owned), with the verify/fixup pass as the actual
  enforcement guarantee.
- Specify the contract precisely enough that whichever of the forward
  dependencies (`feature/source-workspace-clone` / `add-multi-repo-
  projects` #163 and `add-editions-and-licensing` #164) lands first,
  this change's workstreams are the next slice — not pretend the
  dependencies are already satisfied.
- Pass the no-AI-attribution gate unchanged (by being naturally
  distinct from its patterns), and pin that non-collision as a
  regression test.

**Non-Goals:**

- Implementing `feature/source-workspace-clone` or
  `add-multi-repo-projects` itself — this change specifies against
  their eventual shape, does not build them.
- Implementing the `editions` gate mechanism itself — owned by
  `add-editions-and-licensing`; this change only consumes it.
- Building a generic git-host API client — the PR-annotation port is
  narrow (update PR description only).
- Mission-epic implementation (`Comuki-Mission` trailer is conditional
  on the epic, not built here).
- Modifying `ProfilesProvider.cs` itself — Layer 1 adds a sibling
  preparer called alongside it, never modifies it.

## Two-layer enforcement — the central design decision

### Layer 1 — deterministic workspace preparation (before pi starts)

Translator-owned, same place `ProfilesProvider` already prepares the
`profiles/` material. Implemented as a **sibling preparer** that the
Translator loop calls alongside (not inside) `ProfilesProvider` — same
"write a file into the workspace before pi starts" shape as
`ProfilesProvider.PrepareAsync`, separate code path, separate file.

The preparer does two things:

1. **Set the source workspace's git identity via environment variables**
   exported into the `pi` child process:
   - `GIT_AUTHOR_NAME`
   - `GIT_AUTHOR_EMAIL`
   - `GIT_COMMITTER_NAME`
   - `GIT_COMMITTER_EMAIL`

   Git reads these ahead of `user.name` / `user.email` config, and env
   vars need no working-tree file mutation. Resolved from the
   repository's bot identity (R4 credential once it exists). The
   resolution is specified as a named port, `IRepositoryBotIdentity`,
   that the current codebase does not yet have a caller for — see
   "Open Question 1" for the interim shape until R4 lands.

2. **Install a `prepare-commit-msg` git hook** as a plain shell/POSIX
   script written into `.git/hooks/prepare-commit-msg` in the cloned
   workspace. The hook appends the four trailers to every commit
   message the agent produces so compliance does not depend on the
   agent remembering to type them. The trailer values are baked into
   the hook script's literal text at workspace-prep time — Translator
   already knows all four before spawning pi (see "Field-by-field
   trailer resolution" below) — the hook itself is dumb, it never
   re-derives anything at commit time.

Layer 1 sits in `platform/src/host/Comuki.Host.Translator/Attribution/
Hooks/**` (new folder). No edit to `ProfilesProvider.cs`.

### Layer 2 — deterministic verification / fixup (after pi exits)

Translator-owned, runs in the Translator loop **between step 5 ("send
the final StageReport") and step 7 ("else complete on success or fail
with a reason")** of the existing `worker-runtime/spec.md`'s
"Translator loop" requirement. This is the actual enforcement — the
hook is defense-in-depth, not the guarantee.

Layer 2:

1. Reads commits reachable from `HEAD` that are not already on the
   base branch, the same way `ProfilesProvider` reads git state via
   `LibGit2Sharp` (Translator already depends on that package per
   `Comuki.Host.Translator.csproj`).
2. For each such commit: asserts author and committer match the
   resolved bot identity, and the commit message carries all four
   trailers.
3. A commit that fails either check is **amended** (not silently
   re-authored as a new commit — `git commit --amend` preserves the
   tree, only identity / message changes) to the correct identity +
   trailers before completion proceeds.
4. An attribution fixup failure does **not** itself fail an
   otherwise-successful work item — it logs the failure and emits a
   run-journal warning event (`run.attribution_fixup_partial` or
   similar). The reasoning: a worker can produce a fully correct code
   change whose only fault is one missing trailer; failing the item
   over that one trailer would force a retry that re-does the entire
   work. The warning surfaces the gap for review, the run still
   completes, and a follow-up clean fixup or human amend can resolve
   the residue.
5. The verify/fixup pass runs only when a source workspace exists —
   see "Conditions" below. On a run with no source workspace
   (no `Project.SourceGitUrl`, no attachment), the pass is skipped
   entirely, not an error.

Layer 2 sits in `platform/src/host/Comuki.Host.Translator/Attribution/
Verification/**` (new folder).

### Conditions

Both layers gate on **a source workspace existing for the run**. Today
the only path that yields a source workspace is `feature/source-workspace-
clone` / `add-multi-repo-projects` (neither has landed on `master` as of
this branch — see the verified state above). The Translator loop's
existing workspace-preparation step 2 already has a "no profiles source
configured → warn and skip" branch; this change extends the same
shape: no source workspace → no attribution work, no error.

The change's normative claim is the **contract**, not the today-built
implementation: when the source-workspace concept lands, both layers
are required to fire; until it lands, neither fires.

## Field-by-field trailer resolution

The Translator knows all four trailer values before spawning pi. The
values are baked into the hook script's literal text at workspace-prep
time and into Layer 2's assertion table.

### `Generated-by: Comuki vX.Y.Z`

- `X.Y.Z` (+ optionally the build sha suffix) comes from
  `ComukiBuildInfo.Read()` reading **the Translator's own entry
  assembly's** `AssemblyInformationalVersionAttribute`. The Translator
  binary is built from this same monorepo at the same commit as
  everything else (worker-runtime spec's "Worker container image"
  requirement), so it reads its own informational version at runtime
  with zero new plumbing — no version needs to be threaded through
  `COMUKI_*` env vars separately from what the Translator already
  knows about itself.
- Format: `Comuki v<informationalVersion>` where `<informationalVersion>`
  is the `1.2.3+abc1234` form `ComukiBuildInfo.Compose` already
  produces. Examples: `Generated-by: Comuki v1.2.3`, or with sha:
  `Generated-by: Comuki v1.2.3+abc1234` (sha is included when the
  build metadata carried one, omitted otherwise — `ComukiBuildInfo`
  already splits on `+`).

### `Comuki-Run: <run-id>`

- The Translator already has `runId` from `StageStart` (worker-runtime
  spec: "`Start` (StageStart: workItemId, runId, brief)") before it
  spawns pi. No new plumbing.

### `Comuki-Mission: <mission-id>` — conditional

- **Open dependency, named explicitly**: `platform/src/engine/Comuki.
  Engine.Orchestration/Domain/Runs/Run.cs` has no `MissionId` field
  today (Mission is a separate, not-yet-landed epic — `add-mission-
  cowork` / `add-multi-repo-projects` R15's HomeProject/MissionParticipation
  split). The trailer is therefore specified as **conditional**:
  - **WHEN** the run belongs to a Mission (post-Mission-epic) **THEN**
    `Comuki-Mission` is emitted with the mission id.
  - **WHEN** the run does not belong to a Mission (no Mission concept
    attached, or the epic has not landed) **THEN** the trailer is
    omitted entirely (never emitted empty, never emitted blank, never
    emitted as `Comuki-Mission: ` or `Comuki-Mission: unknown`).
- The hook script and the verify/fixup assertion both omit the
  trailer in the no-Mission case; the layer-2 assertion accepts
  "trailer absent" as valid **only when** the run is not Mission-
  scoped.

### `Requested-by: <human>`

- **No single field carries this today.** `Run.cs` has no
  originating-actor field. The resolution table is specified as a
  normative requirement rather than assuming a field exists:
  - **Intake-originated runs**: `IncomingTicket.Author`
    (`platform/src/modules/Intake/Comuki.Modules.Intake.Domain/Tickets/
    IncomingTicket.cs:40` — "Author login / display name on the
    source tracker"). Use the tracker login / display name verbatim.
  - **Chat / dashboard-originated runs**: the authenticated subject,
    resolved through the same shape
    `RequiresPermissionFilter.cs:57` uses for `context.HttpContext.User`
    in the identity module — typically
    `ClaimsPrincipal.Identity?.Name` or a configured display-name
    claim. The exact claim is implementation-defined; what is
    normative is "the authenticated subject the request reached
    Comuki under."
  - **Scheduled-job-originated runs** (see `add-scheduled-jobs/
    tasks.md`'s `IScheduledRunLauncher` brief shape `{ goal,
    scheduleId, scheduleSlug, firedAt }`): a fixed system identity,
    `comuki-scheduler`. This is a literal string; it is not a human
    name and never pretends to be.
- `Requested-by: <human name/handle>` is deliberately **not** spelled
  `Co-Authored-By:` because that prefix would be picked up by the
  AI-attribution gate's `co-authored-by:` pattern as a co-author
  line. Naming the trailer `Requested-by:` is a real design choice,
  not a naming accident — it must not be renamed without
  re-running the no-collision check against `commit-lint.mjs`.
- A task item (workstream 1 in `tasks.md`) flags that `Run` (or the
  work-item brief) needs a `RequestedBy` value threaded through from
  admission to `StageStart`'s brief, since it does not exist on the
  aggregate today.

### Trailer formatting

- Standard git trailer block: blank line, then one trailer per line,
  `Key: Value`, in the order `Generated-by`, `Comuki-Run`,
  `Comuki-Mission` (when present), `Requested-by`.
- `Layer 2` reads the trailer block via `LibGit2Sharp`'s
  `Commit.Message` (the full message including the trailer block, not
  the parsed trailers API — the parse-tolerance question goes away
  if we string-match the four substrings on the message text).
- The hook script appends via `cat >> .git/COMMIT_EDITMSG` after the
  existing body — a POSIX shell append, no `git interpret-trailers`
  dependency.

## PR description footer — PATCH-after-create

Since Comuki has no PR-creation API call today, the footer cannot be
stamped at PR creation. Specify a **PATCH-after-create** step: once
the agent (via `gh pr create` or equivalent, itself) or a human has
opened the PR and its URL reaches Comuki (the existing merge-queue
`PullRequestUrls` ingestion point — `MergeQueueEntry` / `MergeBatch`
in `Comuki.Engine.Orchestration.Domain.MergeQueue`), Comuki calls the
git host's update-PR-description API to append the footer if it is
not already present, rather than trusting the agent's PR body to
contain it.

### Footer shape

- One line, blank line preceding, exact text:
  ```
  
  ---
  🤖 Generated by Comuki — run [link](<PublicUrl>/projects/<projectId>/runs/<runId>)
  ```
- The link target is built from `AuthPublicHostOptions.PublicUrl`
  (`COMUKI_PUBLIC_HOST_URL`, the existing "operator-configured public
  base URL, never the inbound Host header" pattern — same rationale
  applies here: a link in a customer-facing PR must not be built
  from an attacker-controlled header) plus the run id, shape
  `{PublicUrl}/projects/{projectId}/runs/{runId}`. Reuse the existing
  option, do not invent a second "public URL" config.
- The 🤖 emoji is **deliberately distinct** from the no-AI-attribution
  gate's pattern `^(?:🤖\s*)?generated with\b`: the gate trips on
  the phrase "generated with" (linking the emoji to a vendor/tool by
  name), not on a bare emoji. The footer reads "Generated by Comuki
  — run" (no "with", no vendor name), so the gate does not trip on it
  today. This is verified by inspection against `WHOLE_LINE_PATTERNS`
  in `commit-lint.mjs`.

### IPullRequestAnnotator port

- New port `IPullRequestAnnotator` with one method:
  `Task EnsureFooterAsync(PullRequestUrl url, RunFooter footer,
  CancellationToken ct)`.
- Implementation for GitHub (`GitHubPullRequestAnnotator`): reuses
  `GitHubSettings`'s `(Owner, Repo, ApiBase, ApiTokenEnv)` shape from
  `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/
  Providers/GitHub/GitHubSettings.cs` by reference (do not move or
  duplicate the Intake file). Issues a `PATCH .../repos/{owner}/{repo}
  /pulls/{number}` with the merged body; 2xx and 422 (PR already has
  the footer) are both success; 404 / 410 are skipped with a journal
  warning.
- GitLab / other hosts: out of scope for this change. The port is
  host-agnostic; only the GitHub adapter ships here. Adding GitLab is
  a follow-up change that mirrors the GitHub adapter's shape.
- Host-side, not Translator-side: the annotator reacts to merge-queue
  PR-URL ingestion, not to per-work-item Translator loop events. Files
  land under `platform/src/host/Comuki.Host/Attribution/**` (new
  folder).

## Coexistence with the no-AI-attribution gate

Verified by direct inspection of `scripts/commit-lint.mjs` line 98
(`AI_VENDORS`) and lines 138–169 (`WHOLE_LINE_PATTERNS`,
`INLINE_PATTERNS`):

- `AI_VENDORS` does not contain `"comuki"` (the list is closed and
  vendor-shaped).
- The whole-line patterns match `co-authored-by:` with a vendor token,
  `generated with`, `assisted-by:`, `ai-generated` / `ai-assisted` /
  `ai-authored`, `noreply@anthropic.com`-bearing co-author lines, and
  prose like "co-authored by Claude." None of `Generated-by: Comuki
  vX.Y.Z`, `Comuki-Run:`, `Comuki-Mission:`, `Requested-by:`, or the
  PR footer match any of those patterns.
- The inline patterns match `generated with [<text>](<url>)` /
  `<vendor> code` (with leading whitespace consumed) and
  `noreply@anthropic.com`. Neither applies to the four Comuki trailers
  or the PR footer.
- `Requested-by: <human>` is the trailer name, deliberately not
  `Co-Authored-By:`. The difference is a real design choice — see
  "Field-by-field trailer resolution" above.

**No change to `AI_VENDORS` / `WHOLE_LINE_PATTERNS` / `INLINE_PATTERNS`
in `scripts/commit-lint.mjs` is required** for these exact trailer
names to survive the hook as-is. The "allowlisted" language in issue
#165 is satisfied by this being naturally true, not by adding an
allowlist mechanism.

A pinned regression test in `scripts/commit-lint.test.mjs` asserts
that `stripAttribution` leaves a commit message containing all four
trailers byte-for-byte unchanged, so a future edit to `AI_VENDORS`
(e.g. someone reflexively adding "comuki" thinking it's a vendor)
cannot silently break this without a failing test.

## Edition gate — white-label toggle (cross-references `editions`)

Issue #165: attribution "cannot be disabled in community edition, can
be hidden / customized (white-label) in paid — depends on #164."

The white-label toggle is **a field on the repository policy** (R3,
`RepositoryPolicy` aggregate in `add-multi-repo-projects/specs/
repositories/spec.md`'s "Repository owns its rules and its one merge
queue" requirement): `AllowHiddenAttribution: bool`, default `false`.
The toggle's effect is gated behind a named feature key,
`Features.CommitAttributionWhiteLabel`, a `Comuki.Shared.Editions.
Features` registry entry. The `Features` registry and the
`[RequiresFeature]` / `IEdition` gate shape are `add-editions-and-
licensing`'s deliverable, authored in parallel this session — this
change consumes them by name, does not invent their API.

Community edition: the toggle is **unavailable** — the policy field
exists but is immutable at `false`; an endpoint / UI attempting to
set it returns `edition.feature_unavailable`. Attribution stays on,
always. This is a forward dependency on `add-editions-and-licensing` —
named in `tasks.md`'s `Deps:` line for workstream 5.

When the white-label toggle is set AND a paid edition covers it,
attribution is hidden / replaced per a per-repository
`AttributionReplacement` policy field (separate `RepoSetting`,
still owned by R3's `RepositoryPolicy`): one of `none` (omit the
trailers and the PR footer), or `replace: <string-template>` (replace
the trailers with a repository-defined string template). The exact
replacement grammar is left to the implementing workstream in
`tasks.md`; this spec only requires that the toggle exists and
requires a paid edition to activate.

## Verification & observability

- A run-journal event `run.attribution_applied` is emitted after
  Layer 2's verify/fixup pass completes (with a count of commits
  amended and the trailer values used), wired the same way as the
  existing `run.artifacts_bundled` event pattern.
- On attribution fixup failure (some commits could not be amended to
  satisfy the contract), `run.attribution_fixup_partial` is emitted
  with the offending commit SHAs, and the work item is **not** failed
  — see Layer 2 step 4 above for why.
- An OpenTelemetry metric `comuki.attribution.commits.amended` (counter)
  and `comuki.attribution.commits.verified_clean` (counter) live in
  the same `Attribution/**` folder and follow the existing OTel
  convention (`*.comuki.*` lowercase dot.case — see
  `observability/diagnostics.md`).

## Open questions

These are design questions this change does not answer cleanly. They
are flagged here so the implementing workstreams pick them up, not
hidden.

### Open Question 1 — interim bot-identity source until R4 lands

`RepositoryCredentialRef` (R4) does not exist on `master` today — the
`add-multi-repo-projects` change defines it, but has not merged. Layer
1 / Layer 2 both need *some* bot identity to resolve against until R4
ships. Two candidate interim shapes:

- **(a) Fixed-default identity.** A new `ProjectSettings` boolean /
  string field (e.g. `Attribution.IdentityOverride?: string`) and a
  module-default identity `{ name: "Comuki", email:
  "noreply@comuki.dev" }`. Operators override per-project when needed;
  default is used otherwise. Mirrors `KnowledgeEnabled` /
  `VerifyEnabled`'s boolean-opt-in shape.
- **(b) `IServiceProvider`-resolved default at Translator boot.** Read
  a `ComukiOptions.CommitIdentity` option, default
  `{ name: "Comuki", email: "noreply@comuki.dev" }`, no per-project
  override. Simpler but no per-repository differentiation until R4
  lands.

**Default for tasks.md**: option (a) — per-project override is more
honest about the gap. Whichever is picked, the `IRepositoryBotIdentity`
port's interim implementation is named explicitly so it can be
replaced wholesale when R4 lands without changing Layer 1 / Layer 2
callers.

### Open Question 2 — git-host API client library

`IPullRequestAnnotator`'s GitHub adapter issues a `PATCH .../pulls/
{number}`. Choices:

- **Refit** (`agents/comuki-worker-sdk` / `http-resilience-refit.md`
  Pinned) — the standard outbound HTTP path. New Refit interface
  `IGitHubPullsClient` with a `PATCH /repos/{owner}/{repo}/pulls/
  {number}` method. Carrier of the bearer token from the R4 (or
  interim) credential.
- **Raw `HttpClient` via `IHttpClientFactory`** — discouraged by
  `http-resilience-refit.md` §1 except where Refit can't express
  the verb / shape. PATCH on a typed JSON is expressible in Refit.
- **Octokit.NET** — official GitHub SDK; adds a dependency; wider
  surface than this change needs.

**Default for tasks.md**: Refit (option 1), matching the rest of
Comuki's outbound HTTP. Rationale: smallest delta from existing
patterns, and the bearer-token plumbing is already established for
Intake's GitHub adapter — reuse the auth shape, add a new typed
client interface.

### Open Question 3 — `RequestedBy` threading from admission to StageStart

No field on `Run` (or on `StageStart`'s brief) carries the originating
actor today. Options:

- **(a) Add `Run.RequestedBy: string?`** (or as a value object) and
  thread it through admission (`IncomingTicket.Author`,
  chat/dashboard subject, `comuki-scheduler` for scheduled) into
  `StageStart`'s brief. Cleanest, requires touching `Run`,
  admission paths, and `StageStart`.
- **(b) Carry it on the work-item brief only** (not on `Run`); the
  brief is what Translator sees at spawn time, and the brief is what
  the hook script bakes into the trailers. Smaller delta, but the
  attribution then does not appear on a later human-initiated
  follow-up run that is not brief-aware.
- **(c) Defer to a follow-up change** and ship this one with
  `Requested-by: unknown` until the threading exists. Honest about
  the gap but produces ugly output.

**Default for tasks.md**: option (a) with a minimal
`Run.RequestedBy: string?` field and a `Run.RequestedBy` set in
admission. The admission-side edits are small (`IncomingTicket.Author`
is already populated, `IScheduledRunLauncher` knows its origin, chat
identity exists). The risk: `Run` is a stable aggregate and adding
a nullable field there triggers a non-destructive migration, which is
fine but not free.

### Open Question 4 — what happens when `git commit --amend` is itself disallowed

Layer 2 amend uses `git commit --amend` to preserve the tree. The
edge cases where amend is not safe (a `pre-commit` hook that rejects
amend, an agent-managed branch that reverts on amend, etc.) are
not handled by the current design — Layer 2 logs the failure and
emits a journal warning, but does not retry, reauthor as a new
commit, or fall back to a manual amend script. A future change may
need to handle these; this change names them and moves on.

### Open Question 5 — credential resolution timing for the hook

The hook script is written at workspace-prep time with literal text.
If the credential rotation model is "token expires mid-run" (the
R4 design has not finalized this), the hook's baked identity is
still valid for the run (committer identity is per-commit, not per-
push). But the PR footer PATCH-after-create uses the live credential,
and that one must be re-resolved at PR-annotation time, not
workspace-prep time. Tasks.md workstream 4 must not reuse the
workspace-prep-time credential for the PATCH call.

## Decisions

### D1. Two-layer enforcement, not one

**Choice:** workspace-prep hook + post-pi verify/fixup pass.

**Why:** the hook alone depends on agent cooperation (it does not run
if the agent uses `git commit --no-verify`, and it cannot intercept
non-commit git operations like `git rebase --continue`); the
verify/fixup pass alone works without the hook but is one-fixup-
per-commit. Together: the hook reduces how often verify/fixup has to
do real work, and verify/fixup is the actual guarantee. Mirrors
"LLM proposes — system disposes" (`openspec/config.yaml` `context:`).

**Rejected:** single-layer hook only (depends on cooperation);
single-layer verify/fixup only (works but wasteful — every commit
gets round-tripped through amend even when the hook did its job);
agent instruction in the brief only (no system guarantee at all).

### D2. Verify/fixup uses `git commit --amend`, not `git rebase`

**Choice:** non-conforming commits are amended, not rebased.

**Why:** `git commit --amend` preserves the tree (only identity and
message change), so the agent's code change is not disturbed by the
fixup. A rebase would re-apply the commit's tree (re-running any
post-commit hooks or worktree state) and could mutate what the agent
produced. Amend is the surgical tool for "change the message and the
identity, keep the tree."

**Rejected:** rebasing the affected commit (mutates the agent's
work); silently rewriting as a new commit (loses the original SHA,
breaks any downstream tools that pinned the SHA).

### D3. PATCH-after-create, not PR-creation

**Choice:** Comuki does not call `POST /repos/.../pulls`; it
appends the footer to a PR that already exists.

**Why:** Comuki has no PR-creation call today, and adding one is a
larger surface than this change needs. The footer is the only
required Comuki-side PR metadata; the agent or human opens the PR
itself (the agent already shells out to `gh` or equivalent). This
also matches the existing `MergeQueueEntry.PullRequestUrls` ingestion
shape — Comuki sees the URL after the PR exists and reacts.

**Rejected:** full PR-creation call (broader git-host API surface,
agent credential concerns); requiring the agent to compose the
footer in the PR body (depends on cooperation — exactly what
two-layer enforcement is meant to avoid).

### D4. `Requested-by:`, not `Co-Authored-By:`

**Choice:** the human-attribution trailer is named `Requested-by:`.

**Why:** the no-AI-attribution gate's `WHOLE_LINE_PATTERNS` includes
`^co-authored-by:.*?\b<vendor>` and prose like "co-authored by
Claude." Spelling the trailer `Co-Authored-By:` would risk false
positives on the gate if a future commit ever listed a vendor in
the same message; using a distinct name sidesteps that. The shape
`Key: Value` with `Key=Requested-by` is still a valid git trailer
and the verify/fixup pass treats it the same.

**Rejected:** `Co-Authored-By: <human>` (false-positive risk on the
gate); embedding the requestor as a `Signed-off-by:` (wrong
semantic — DCO sign-off is a legal claim about contribution, not a
provenance marker).

### D5. White-label toggle lives on `RepositoryPolicy`, not on a Project

**Choice:** `AllowHiddenAttribution: bool` is a field of
`RepositoryPolicy` (R3, in the `repositories` capability owned by
`add-multi-repo-projects`).

**Why:** the issue language says "Configured in the repository
policy (R3)." Attribution is repository-scoped: a service repo and
its docs repo may have different white-label settings under the
same Project. A Project-scope toggle would force one policy across
all attached repositories, which is exactly the duplication R3 is
designed to prevent.

**Rejected:** Project-scoped toggle (duplicates per attached
repository; contradicts R3); global Comuki setting (no per-
repository differentiation).

## Cross-capability references (not modified by this change)

- **`editions`** (sibling change `add-editions-and-licensing`, #164,
  authored in parallel this session): the `Features.*` registry and
  `[RequiresFeature]` / `IEdition` gate shape this change's white-
  label toggle consumes. Reference by name and shape only; do not
  invent the full API here.
- **`repositories`** (`add-multi-repo-projects`, #163): the
  `Repository` registry, `RepositoryPolicy`, `RepositoryCredentialRef`
  (R4), and `ProjectRepositoryAttachment` whose eventual landing
  turns Layer 1 / Layer 2 from "specifies against future shape" to
  "binds to real credentials." Reference by name and shape only.
- **`merge-queue`** (no dedicated openspec capability today; the
  engine's `MergeQueueEntry`/`MergeBatch` are documented inline in
  `add-multi-repo-projects/design.md`): the `IPullRequestAnnotator`
  port reacts to PR URLs ingested via this path. No spec change
  required; the annotator is a new host-side adapter that hooks into
  the existing ingestion.

## Related

- `proposal.md` — the why / what / capabilities / impact / non-goals
- `tasks.md` — the workstreams, sized for independent MiniMax agents
- `specs/commit-attribution/spec.md` — the normative requirements
- `specs/worker-runtime/spec.md` — the additive requirements on the
  Translator loop
- `add-multi-repo-projects/decisions.md` — R3, R4, R8 (bot identity
  belongs to the repository; one credential per repository; access
  level is `min(attachment, credential)`)
- `add-editions-and-licensing` — the `editions` capability consumed
  by the white-label toggle
- `openspec/specs/worker-runtime/spec.md` — the Translator loop
  this change inserts two steps into
- `scripts/commit-lint.mjs` — the no-AI-attribution gate
- `~/.agents/rules/process/no-ai-attribution.md` — the rule the gate
  implements