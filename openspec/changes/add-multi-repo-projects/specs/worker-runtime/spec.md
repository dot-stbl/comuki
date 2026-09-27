## ADDED Requirements

### Requirement: Multi-repository workspace composition
A worker claim SHALL resolve to exactly one read-write target repository —
the task's target, from its `ProjectRepositoryAttachment` (or, during the
migration window, the scalar `Project.SourceGitUrl`/`SourceGitRef`) —
plus its confirmed-link graph neighbours read-only, capped at a configured
neighbour limit (R8). This is additive to the single-repository workspace
introduced by `feature/source-workspace-clone` (issue #125): the read-write
target SHALL continue to materialize at the working-directory root exactly
as `ISourceWorkspacePreparer` does today; each read-only neighbour SHALL
materialize under `context/<repository-slug>/`, cloned at the neighbour's
default branch or the link's pinned ref when one is declared. A worker
process SHALL NEVER receive a write-capable credential for a neighbour
repository — only the read-write target's resolved credential is present in
the claim payload.

#### Scenario: Single-repo claim is unaffected
- **WHEN** a task's target repository has no confirmed graph neighbours
- **THEN** the workspace materializes exactly as `feature/source-workspace-clone`
  today — one repository cloned at the working-directory root, no
  `context/` subdirectories

#### Scenario: Neighbour repositories are read-only and uncredentialed
- **WHEN** a task's target repository has two confirmed-link neighbours
  within the configured cap
- **THEN** both neighbours materialize under `context/<slug>/` and the claim
  payload carries no write-capable credential for either

#### Scenario: Neighbour count above the cap is truncated, not rejected
- **WHEN** a task's target repository has more confirmed-link neighbours than
  the configured cap
- **THEN** the workspace materializes the target plus neighbours up to the
  cap and the task proceeds — an over-cap graph never blocks the claim
