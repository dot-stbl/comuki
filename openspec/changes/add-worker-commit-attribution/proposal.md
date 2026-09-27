## Why

Every commit/PR a Comuki worker produces today carries the agent's own
author identity and an arbitrary message, with no provenance back to the
run, mission, or requesting human — `git log --author` cannot answer
"what did the platform change and who asked for it." Issue #165 binds the
fix: workers commit/PR as the repository's bot identity (R4) with
`Generated-by: Comuki vX.Y.Z`, `Comuki-Run`, `Comuki-Mission`,
`Requested-by` trailers and a PR-footer run link — coexisting with this
repo's no-AI-attribution gate (Comuki trailers are naturally distinct
from the AI-vendor patterns the gate trips on).

## What Changes

- New `commit-attribution` capability: fixes commit author+committer to
  the repository's bot identity (R4, once it lands), enforces four
  trailers on every worker commit, stamps a run-link footer on every PR
  the merge queue ingests.
- The contract is **two deterministic layers** Translator owns around
  the agent's run: pre-spawn workspace prep (env-var identity +
  `prepare-commit-msg` hook, defense-in-depth) and a pre-completion
  verify/fixup pass that amends any non-conforming commit (the actual
  guarantee — matches "LLM proposes, system disposes").
- A `Requested-by` resolution table covering intake/chat/dashboard/
  scheduled-originated runs (no single `Run` field exists today —
  flagged as a task item to thread `RequestedBy` to `StageStart`).
- `Comuki-Mission` is conditional: emitted only once the Mission epic
  lands; never emitted blank.
- A white-label toggle on repository policy ("hide/customize
  attribution") gated by `editions` (sibling `add-editions-and-
  licensing`); Community has no toggle at all.
- `worker-runtime` gets the two hook points as additive requirements
  (no removal of existing Translator loop steps).

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
unblock them: (1) the product-source clone concept
(`feature/source-workspace-clone` #125 / `add-multi-repo-projects` #163's
`Repository`/`RepositoryCredentialRef`, R4) has not landed on `master` —
verified by grep; Layer 1/2 are specified **against that future shape**,
with an interim bot-identity resolver (`design.md` Open Question 1) until
R4 replaces it. (2) `add-editions-and-licensing` (#164, sibling authored
in parallel) is the producer of `Features.CommitAttributionWhiteLabel`
and the gate this change consumes.

The existing `ProfilesProvider` is extended, not rewritten, with a
sibling preparer; the merge queue's existing `PullRequestUrls` ingestion
is extended with a Host-side `IPullRequestAnnotator`. New tests include a
pinned regression in `scripts/commit-lint.test.mjs` for the AI-vendor
non-collision guarantee.

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