## Why

Every git commit and pull request a Comuki worker produces today carries the
agent's own author identity and an arbitrary commit message, with no
provenance back to the run, the mission, or the human that requested it.
A reviewer's `git log` cannot tell Comuki commits from a developer's
local ones, and a `git log --author` audit cannot answer "what did the
platform change last Tuesday and who asked for it." Issue #165 binds the
fix: Comuki workers must commit and PR as the repository's bot identity
(R4) with `Generated-by: Comuki vX.Y.Z`, `Comuki-Run`, `Comuki-Mission`,
and `Requested-by` trailers, and a PR-description footer linking back to
the run — coexisting with this repo's no-AI-attribution gate (Comuki
trailers are allowlisted by being naturally distinct from the AI-vendor
patterns the gate trips on).

## What Changes

- Specify a new `commit-attribution` capability that fixes commit author
  + committer to the repository's bot identity (R4, once it lands),
  enforces four trailers on every commit a worker produces, and stamps
  a run-link footer on every PR the merge queue ingests.
- Specify the attribution contract as **two deterministic layers**
  Translator owns around the agent's run: pre-spawn workspace
  preparation (env-var identity + `prepare-commit-msg` hook), and a
  pre-completion verify/fixup pass that amends any non-conforming
  commit. The hook is defense-in-depth; the verify/fixup pass is the
  guarantee, matching the "LLM proposes — system disposes" line in
  `openspec/config.yaml`.
- Specify a `Requested-by` resolution table that covers intake-, chat-,
  dashboard-, and scheduled-originated runs (no single Run field exists
  today — flag as a task item to thread `RequestedBy` through to
  `StageStart`).
- Specify `Comuki-Mission` as conditional: emitted only when the
  Mission epic has landed; never emitted blank.
- Specify a white-label toggle on the repository policy: "hide /
  customize attribution" is gated by `editions` (sibling change
  `add-editions-and-licensing`); community edition has no toggle at all.
- Extend `worker-runtime` with the two hook points as additive
  requirements (no removal of existing Translator loop steps).

## Capabilities

### New Capabilities

- `commit-attribution`: identity resolution, trailer enforcement
  (two-layer), PR-footer PATCH-after-create, edition-gated white-label.

### Modified Capabilities

- `worker-runtime`: the Translator loop gets two additive steps — Layer 1
  workspace-prep hook install (before pi spawns), Layer 2 verify/fixup
  (after `StageReport`, before `complete`/`fail`).

## Impact

Two forward dependencies, named explicitly because this change does not
unblock them:

1. The product-source clone concept (`feature/source-workspace-clone`,
   #125 / `add-multi-repo-projects` #163's `Repository` + attachment +
   `RepositoryCredentialRef`, R4) has not landed on `master` as of this
   branch — verified by grep. This change specifies Layer 1 / Layer 2
   **against that future shape**; the today-attempted bot identity is
   an interim resolver (named, see `design.md` Open Question 1) until
   R4 lands and replaces it.
2. `add-editions-and-licensing` (#164, sibling authored in parallel this
   session) is the producer of `Features.CommitAttributionWhiteLabel`
   and the `[RequiresFeature]`/`IEdition` gate this change consumes; the
   white-label toggle's spec section is conditional on that change
   landing.

The Translator loop's existing `ProfilesProvider` is extended, not
rewritten, with a sibling preparer. The merge queue's existing
`PullRequestUrls` ingestion is extended, not rewritten, with a Host-side
`IPullRequestAnnotator`. New tests include a pinned regression in
`scripts/commit-lint.test.mjs` for the AI-vendor non-collision guarantee.

## Non-goals

- Building `feature/source-workspace-clone` or `add-multi-repo-projects`
  itself — this change specifies against their eventual shape, does not
  build them.
- Implementing the `editions` gate mechanism — owned by
  `add-editions-and-licensing`; this change only consumes the
  `Features.CommitAttributionWhiteLabel` shape.
- A generic git-host API client — the PR-annotation port is narrow
  (PATCH description only); broader write surface is a later change.
- Mission-epic implementation (the `Comuki-Mission` trailer is
  conditional on it, not built here).
- Touching `ProfilesProvider.cs` itself — Layer 1 adds a sibling
  preparer called alongside it, never modifies it.