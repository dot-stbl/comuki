## Why

Comuki currently scopes execution to one Project with, at most, one scalar
product-source repository (`Project.SourceGitUrl`/`SourceGitRef`, landing via
`feature/source-workspace-clone`, issue #125) and one worker-profiles
repository (`Project.ProfilesGitUrl`/`ProfilesGitRef`). Real client work
spans multiple repositories with independent ownership, branch protection,
and merge queues — a service repo, its frontend, a deploy/GitOps repo, a
shared library, a docs repo — and those repositories are frequently shared
across more than one Comuki Project (a platform team's shared library
attached to several product Projects). Today Comuki has no way to register a
repository as a standalone unit, attach it to more than one Project, express
a link between two repositories, wait for a cross-repo artifact before
merging a downstream change, or safely decline to touch a repository it has
no write access to. Issue #50's dependency-ordered merge batches and the
mission-cowork epic's single-Project Mission invariant both assume the
one-Project-one-repo world this change replaces.

## What Changes

- Introduce a new `Comuki.Modules.Repositories` module owning the
  **Repository** registry (url, host, credential reference, policy),
  repo-owned rules (protected branches, required checks, approvers, one
  merge queue per repository), and repo-to-repo **RepositoryLink**s
  (package/pin, submodule, api-contract, deploy/GitOps, codegen-consumer,
  read-context, fork/upstream) with `suggested`/`confirmed` status.
- Introduce **ProjectRepositoryAttachment** in the Projects module: a
  many-to-many Project↔Repository edge carrying a role (primary, service,
  frontend, library, deploy-gitops, docs, …) and an access level
  (`write` | `read` | `external`); effective access is
  `min(attachment.access, credential.access)`.
- Introduce **ArtifactSource** adapters (NuGet/npm feed, container registry,
  git tag, merge-to-branch, CI status) that resolve link version constraints
  via webhook where available, else poll, and gate cross-repo DAG
  progression.
- Introduce the **ExternalChangeRequest** flow: when a needed change targets
  a repository the Project has no write attachment for (`access: external`),
  Comuki never edits it — it sends a brain-authored capability request
  through the repository's configured request channel, marks the owning task
  `blocked-external`, watches the artifact for the capability to appear, and
  verifies before unblocking.
- Introduce **link auto-discovery** on attach and periodically (`.gitmodules`,
  `PackageReference`/`package.json`, OpenAPI clients, helm/kustomize values,
  fork remotes) producing `suggested` links; only human-confirmed links drive
  planning and the cross-repo DAG.
- Introduce **multi-project Mission participation** routing: work on a
  repository routes through the Mission's home Project's write attachment
  when it has one; otherwise the Brain invites a participating Project that
  holds a write attachment (that Project's own approval, via the capability
  broker, and its own budget); otherwise the change is `blocked-external`.
  Merge always follows the target repository's own rules regardless of which
  Project initiated the work.
- Introduce **worker workspace composition**: one task claims exactly one
  target repository read-write plus a capped set of graph-neighbour
  repositories read-only for context; a cross-repo change becomes several
  DAG tasks, one PR each; a submodule pointer bump is a mechanical step with
  no worker.
- Introduce a **repository memory layer** alongside the existing
  user/project/global scopes: facts intrinsic to the repository and its code
  (shared by every Project attaching it) plus incidents mapped in via deploy
  links; project-originated context never enters repo memory except through
  a redacted, provenanced promotion proposal (mirrors the epic's private
  Mission → Project memory declassification pattern).
- Extend **Brain visibility**: an attached repository is fully visible; a
  graph neighbour's metadata (name, link type, artifact/version, public
  contract surface, owning Projects) is always visible so the Brain can
  detect "a change is needed there"; a neighbour's code or memory is visible
  only with the initiating attachment's read access.
- **Subsumes issue #50** (dependency-ordered merge batches): the flat
  `MergeBatch.PullRequestUrls` list is superseded by one merge queue per
  repository plus the cross-repo DAG's artifact-wait ordering — no separate
  cross-batch dependency feature is built.
- **Migration:** `feature/source-workspace-clone`'s scalar
  `Project.SourceGitUrl`/`SourceGitRef` lands as-is (out of this change's
  scope) and is migrated, once both changes exist, into an auto-created
  primary `ProjectRepositoryAttachment` (role `primary`, access `write`)
  pointing at a newly registered `Repository` row; the scalar fields are then
  deprecated in favor of the attachment. `Project.ProfilesGitUrl`/
  `ProfilesGitRef` (the worker-profiles overlay) are untouched — they are not
  a product repository and stay scalar.
- **Amends the mission-cowork epic** (tracked separately in
  `add-mission-cowork/specs/missions/spec.md` and `decomposition.md`, in the
  same commit as this change): "A Mission SHALL belong to exactly one
  Project" becomes a home Project plus zero or more participating Projects.

## Capabilities

### New Capabilities

- `repositories`: Repository registry, policy, credential references,
  repo-to-repo links, link auto-discovery, artifact-source adapters,
  cross-repo DAG artifact-wait, external-change requests, repository memory
  layer, and Brain visibility rules.

### Modified Capabilities

- `projects`: adds `ProjectRepositoryAttachment` (role, access) as a
  many-to-many Project↔Repository edge and the migration path off the scalar
  `SourceGitUrl`/`SourceGitRef` fields.
- `worker-runtime`: evolves the single-repository workspace clone (from
  `feature/source-workspace-clone`) into a multi-repository workspace: one
  read-write target repository plus capped read-only graph-neighbour
  repositories.

## Impact

Touches the new Repositories module, the Projects module (attachments +
scalar-field migration), the worker-runtime workspace-preparation path
(Translator `ISourceWorkspacePreparer` / Host `ClaimWorkspace`), the
Orchestration engine's merge queue (repo-scoped queues, DAG artifact-wait),
Memory (repository-scoped facts, deferred to a later child change), and
Brain context assembly (graph-neighbour metadata visibility). Also amends
`add-mission-cowork`'s `missions` capability (home/participating Project
split) and its `decomposition.md` wave plan (new child change #163, slotted
in Wave 2 alongside #88, foundation depending only on #87).

## Non-goals

- Building a generic package-manager or CI system; `ArtifactSource` adapters
  only observe readiness, they do not publish.
- Cross-repository distributed transactions; the DAG is choreographed
  through artifact readiness and per-repo merges, never a multi-repo atomic
  commit.
- Granting Comuki write access to a repository it was not explicitly
  attached to with `write` access — the external-block path is mandatory,
  never bypassed by capability level.
- Replacing a repository's own branch-protection/CI/approval configuration —
  Comuki reads and respects it, it does not own it.
- Full Mission-participation invitation/approval/budget mechanics living in
  `missions/spec.md` itself — the epic amendment only introduces the
  `HomeProject`/`MissionParticipation` structure (empty participants =
  single-Project case); this change's `repositories` capability specifies
  the routing behavior that structure enables.
