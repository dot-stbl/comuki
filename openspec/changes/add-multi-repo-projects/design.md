## Context

See `proposal.md` for motivation, `decisions.md` for the verbatim R1–R16 user
decisions this design traces to, and `specs/` for normative behavior. Today:

- A Project owns at most one product repository as a scalar pair
  (`Project.SourceGitUrl`/`SourceGitRef`, landing via
  `feature/source-workspace-clone`/#125) and one worker-profiles repository
  (`Project.ProfilesGitUrl`/`ProfilesGitRef`, both `Comuki.Modules.Projects.Domain.Projects.Project`).
  These are independent overlays: profiles are the agent-brief material
  copied under `profiles/`; source is the product checkout the agent works
  in (`Comuki.Host.Translator.Execution.Workspace.ISourceWorkspacePreparer`
  clones `SourceGitUrl@SourceGitRef` into the working-directory root; the
  claim payload is `Comuki.Host.Workers.Workspace.ClaimWorkspace(SourceGitUrl,
  SourceGitRef, GitCredential)` — one repo, one credential, one workspace).
- `MergeQueueEntry.ProjectId` is already nullable
  (`Comuki.Engine.Orchestration.Domain.MergeQueue.MergeQueueEntry`) —
  the merge queue was already designed to outlive a single-Project
  assumption ("release train scenarios").
- `MergeBatch.PullRequestUrls` is a flat `IReadOnlyList<string>`
  (`Comuki.Engine.Orchestration.Domain.MergeQueue.MergeBatch`) — an
  operator-declared ordered list with no dependency graph. Issue #50
  (dependency-ordered merge batches) has no code today; this change
  subsumes it rather than adding graph structure to `MergeBatch` — see
  "Relationship to issue #50" below.
- No `Repository`, attachment, link, or artifact-adapter concept exists
  anywhere in `platform/src`. Fully greenfield module
  (`Comuki.Modules.Repositories`).
- `add-mission-cowork/specs/missions/spec.md`'s "Mission lifecycle"
  requirement states "A Mission SHALL belong to exactly one Project" — this
  change's Part B amends that file directly (home Project + participating
  Projects) so the epic and this change land coherently in one branch.

The design must preserve the repository's modular-monolith law: sibling
modules do not reference implementations, domain state stays module-owned,
and the host composes adapters. `Comuki.Modules.Repositories` is a sibling of
`Comuki.Modules.Projects`, not a child of it — `ProjectRepositoryAttachment`
lives in Projects (it references `ProjectId` internally and `RepositoryId` by
value, mirroring how orchestration references `ProjectId` today) and reads
Repository facts through a port the host composes, the same shape as the
existing Projects→Compute scale-adapter bridge.

## Goals / Non-Goals

**Goals:**

- Let a Repository be registered once and attached to more than one Project.
- Keep repository rules (branch protection, required checks, approvers, one
  merge queue) and credentials owned by the repository, never duplicated
  per-Project.
- Express repo-to-repo relationships as a link graph with a declared
  handoff artifact per link type, confirmed by a human before it drives
  planning.
- Execute a cross-repo change as a DAG of single-target-repo tasks gated by
  artifact readiness, never a multi-repo atomic operation.
- Refuse to write to a repository without an explicit write attachment —
  route to an external-change request instead, always.
- Let a Mission's work reach a repository through whichever attached
  Project (home or participating) actually holds write access to it.
- Give the Brain graph-wide metadata visibility without granting it
  graph-wide code/memory visibility.

**Non-Goals:**

- A generic CI/CD or package-registry implementation.
- Multi-repo distributed transactions or a shared cross-repo working tree.
- Automatic write access to any repository not explicitly attached with
  `write`.
- Full invitation/approval/budget Mission-participation mechanics inside
  `missions/spec.md` itself (that structural stub is `add-minimal-missions`'s
  job per R15; this change specifies the routing behavior the stub enables).
- Modifying the `memory`, `context-fabric`, or an explicit `merge-queue`
  capability spec in this change — see "Cross-capability references".

## Entities

```text
Repository
  Id, Url, Host (github|gitlab|...), DefaultBranch
  CredentialRef -> RepositoryCredentialRef            (R4)
  Policy -> RepositoryPolicy                           (R3)
  CreatedAt, UpdatedAt

RepositoryPolicy                                       (R3)
  RepositoryId
  ProtectedBranches, RequiredChecks, Approvers
  # ONE merge queue per repository — not modeled as a separate aggregate;
  # the existing MergeQueueEntry/MergeBatch pair becomes repo-scoped
  # (RepositoryId join) instead of (or alongside) ProjectId-scoped.

RepositoryCredentialRef                                (R4)
  RepositoryId
  IntegrationRef (git-host / tracker connection, from Integrations post-#88)
  DefaultAccess: write | read
  # effective access = min(attachment.access, credential.access);
  # an attachment MAY declare a per-attachment credential override
  # (exception path, R4) instead of the repository default.

ProjectRepositoryAttachment                            (R1, lives in Projects)
  ProjectId, RepositoryId
  Role: primary | service | frontend | library | deploy-gitops | docs | ...
  Access: write | read | external
  CredentialOverrideRef?: RepositoryCredentialRef
  CreatedAt

RepositoryLink                                         (R6)
  Id, SourceRepositoryId, TargetRepositoryId
  Type: package-pin | submodule | api-contract | deploy-gitops
        | codegen-consumer | read-context | fork-upstream
  Artifact: the handoff artifact this link type declares
            (package version | commit SHA/submodule pointer
             | contract version | image tag | generated code | upstream merge)
  VersionConstraint: e.g. ">= 2.3.0"
  Status: suggested | confirmed
  DiscoveredBy: manifest-scan | manual
  LastConfirmedAt?, DriftDetectedAt?

ArtifactSource (adapter, not a stored aggregate)        (R9)
  Kind: nuget-feed | npm-feed | container-registry | git-tag
        | merge-to-branch | ci-status
  RepositoryId (or feed identity for package adapters)
  ResolveReadiness(VersionConstraint) -> ready | pending
  Mode: webhook | poll (poll when no webhook integration exists)

ExternalChangeRequest                                  (R2, R5)
  Id, RepositoryId, RequestingProjectId, RequestingTaskId
  Channel: tracker-issue | draft-for-human | fork-pr
  CapabilitySpec: brain-authored description of the needed capability
  Status: sent | awaiting-artifact | verified | unblocked
  WatchedArtifact -> ArtifactSource

MissionParticipation                                    (R7, R15 — see Part B)
  # Structural stub introduced by add-minimal-missions (#93):
  # HomeProjectId (required) + ParticipatingProjectIds (default empty).
  # This change specifies routing over that structure; it does not
  # redefine the structure itself.
```

Naming note: `ArtifactSource` here is a **read adapter over external package
feeds / registries / git refs** used to gate the cross-repo DAG — distinct
from the existing `artifacts` capability, which is the immutable per-run
result/log bundle in MinIO (`IRunArtifactStore`). The two are unrelated; no
rename is intended for either.

## Flows

### 1. Register and attach

1. An operator (or an Integrations-driven discovery, post-#88) registers a
   `Repository` (url, host) with a `RepositoryCredentialRef` and a
   `RepositoryPolicy` snapshot (protected branches / required checks /
   approvers read from the host's API).
2. A Project attaches the Repository via `ProjectRepositoryAttachment`
   (role, access). Effective access is `min(attachment.access,
   credential.DefaultAccess)` unless the attachment carries a
   `CredentialOverrideRef`.
3. Attach triggers link auto-discovery (flow 2) for that repository.

### 2. Link auto-discovery

1. On attach, and periodically thereafter, a discovery pass scans the
   repository's manifests: `.gitmodules` (submodule links),
   `PackageReference`/`package.json` (package/pin links), OpenAPI client
   generation config (api-contract / codegen-consumer links), helm/kustomize
   values (deploy-gitops links), git remotes matching a known fork upstream
   (fork/upstream links).
2. Every discovered edge is written or refreshed as `RepositoryLink` with
   `Status: suggested`. Suggested links are Brain hints only — they never
   drive planning or the DAG.
3. A human confirms a suggested link (`Status: confirmed`) before it can gate
   a DAG task. If a previously confirmed link's manifest evidence disappears
   on a later scan, the link is flagged `DriftDetectedAt` and a notification
   fires — the link stays `confirmed` (last known state) until a human acts.

### 3. Cross-repo DAG with artifact wait

1. A cross-repo change decomposes into one task per target repository —
   never a task spanning two repositories. Each task claims its target
   repository read-write plus graph-neighbour repositories (via confirmed
   links) read-only, capped at a configured neighbour limit (R8).
2. Tasks whose target repository is downstream of a confirmed link wait on
   that link's `ArtifactSource` to report the version constraint satisfied
   (webhook push when the adapter supports it, else poll).
3. Upstream task merges (through its own repository's merge queue, R3) →
   its artifact publishes (package version / tag / merge-to-branch / image
   / generated code) → the downstream task's wait resolves → downstream
   bumps its pin/pointer/generated code → downstream task merges through
   its own repository's merge queue.
4. A submodule-pointer bump is mechanical: no worker is spawned, the pointer
   commit is produced directly and enters that repository's own merge queue
   like any other change.
5. Upstream changes are checked for backward compatibility before merging
   (contract check, R13). If a downstream task then fails anyway, it goes
   back for rework — the upstream merge is never touched by that failure. A
   revert-PR is proposed automatically only when an upstream change is
   confirmed to have broken other consumers, and merges only on approval.

### 4. External block

1. A task needs to change a repository the initiating Project (home or, per
   flow 5, a participating Project) has only `read` or `external` access to.
2. Comuki does not edit that repository. It creates an `ExternalChangeRequest`
   with a Brain-authored `CapabilitySpec` (what capability is needed, e.g.
   "expose endpoint X" or "bump dependency Y to >= 2.0"), sent through the
   repository's configured channel: an issue in its tracker if an
   integration exists, a draft for a human to forward otherwise, or a PR
   from a fork when Comuki has read access to open one.
3. The originating task's status becomes `blocked-external`. An artifact
   watcher (flow 3's `ArtifactSource` machinery, reused) waits for the
   capability's expected signal (a version/tag/merge matching the spec).
4. When the watcher fires, the Brain verifies the capability actually
   satisfies the original `CapabilitySpec` (not just "something merged") —
   only then does the request move to `verified` and the originating task
   unblocks.

### 5. Multi-project Mission participation

1. A Mission has one home Project (budget/memory owner by default) and,
   per the `add-mission-cowork` amendment (Part B), zero or more
   participating Projects (`MissionParticipation`, empty by default =
   today's single-Project Mission, unchanged behavior).
2. Work targeting repository R first tries the home Project's attachment to
   R. If the home Project has a write attachment, the task runs on the home
   Project's budget and memory as today.
3. If the home Project has no write attachment to R, the Brain invites a
   participating Project that does hold a write attachment to R. That
   Project's own approval is required — routed through the capability broker
   (the same actor-bound invocation / policy / audit plane every other
   cross-cutting capability uses, per the epic's `capability-broker`
   change) — and the task then runs on the invited Project's own budget.
4. If no attached-or-participating Project holds a write attachment to R,
   flow 4 (external block) applies.
5. Regardless of which Project's budget paid for the work, the merge always
   follows R's own repository rules (R3) — a Project never overrides another
   repository's branch protection or approvers by initiating work on it.

### 6. Workspace composition

1. A worker claim resolves to exactly one read-write target repository (the
   task's target) plus its confirmed graph neighbours, capped, read-only.
2. The read-write target materializes at the working-directory root — this
   preserves `feature/source-workspace-clone`'s existing single-repo
   behavior (`ISourceWorkspacePreparer` clones there today) once that change
   lands; multi-repo composition is additive, not a rewrite of that path.
3. Each read-only neighbour materializes under `context/<repository-slug>/`,
   cloned shallow at the neighbour's default branch (or the link's pinned
   ref, when the link specifies one), never writable by the agent process.
4. A worker never receives a write credential for a neighbour repository —
   only the target repository's resolved credential is present in the claim.

### 7. Two-layer memory and redaction promotion

1. Repository memory (facts about the code/service/incidents intrinsic to
   the repository, R11) is shared by every Project attaching that
   repository. Project memory (product architecture, infra decisions,
   customer/ticket context) stays Project-scoped as today.
2. Background watchers observing the product (namespaces, dashboards,
   alerts) map incidents to a repository via its deploy-gitops link and
   write the incident to that repository's memory, not the Project's.
3. Repository memory holds only facts derivable from the repository itself
   and its incidents (R12) — Project-originated context (tickets, customers,
   business decisions) never enters repository memory directly.
4. Promotion from Project memory to Repository memory happens only through a
   redacted, provenance-carrying proposal — the same shape as the epic's
   private-Mission → Project-memory declassification requirement
   (`add-mission-cowork/specs/memory/spec.md`'s "Private Mission
   declassification"): the proposal shows the exact future claim, discloses
   provenance, names the target audience (every Project attaching the
   repository), and requires approval under both the source Project's and
   the repository's policy.

### 8. Brain visibility

1. Within a Project A, A's own attached repositories are fully visible to
   the Brain (code + memory, per A's attachment access).
2. Graph neighbours of A's repositories (via confirmed `RepositoryLink`s)
   are always visible at the metadata level — name, link type, artifact/
   version, public contract surface, owning Projects — regardless of A's
   access to that neighbour, so the Brain can recognize "a change is needed
   there" and initiate flow 3, 4, or 5.
3. A neighbour's code or memory is visible to the Brain only when the
   initiating attachment (A's own, or a participating Project's per flow 5)
   carries at least `read` access to that neighbour.

## Migration: scalar `SourceGitUrl`/`SourceGitRef` → primary attachment

`feature/source-workspace-clone` (issue #125) lands independently and as-is:
`Project.SourceGitUrl`/`SourceGitRef` remain scalar fields, and
`ISourceWorkspacePreparer`/`ClaimWorkspace` keep their single-repo shape.
This change does not touch that branch. Once both changes exist in the same
tree, a migration step:

1. For every Project with a non-null `SourceGitUrl`, registers (or matches an
   existing) `Repository` row for that URL.
2. Creates a `ProjectRepositoryAttachment(role: primary, access: write)`
   from the Project to that Repository, carrying `SourceGitRef` as the
   attachment's pinned ref (or the link/attachment's default when null).
3. Deprecates `Project.SourceGitUrl`/`SourceGitRef` as read paths once the
   attachment is authoritative — worker workspace composition (flow 6)
   resolves the read-write target from the primary attachment, falling back
   to the scalar fields only during the migration window.
4. `Project.ProfilesGitUrl`/`ProfilesGitRef` are unaffected — they are not a
   product repository and are out of scope for `Repository`/attachment
   modeling.

## Relationship to issue #50

Issue #50 proposed dependency-ordered merge batches: given `MergeBatch`'s
current flat `PullRequestUrls` list (confirmed by grep — no ordering, no
graph), the fix would have added a dependency graph *inside* one batch.
This change subsumes that need structurally instead: a cross-repo change is
never one batch, it is a DAG of per-repository tasks (flow 3), each merging
through its own repository's merge queue (R3) in the order its artifact
dependencies resolve. `MergeQueueEntry.ProjectId` already being nullable
confirms the merge queue was already designed to outlive a single-Project
assumption; this change extends that same queue to be keyed by
`RepositoryId` (one queue per repository, R3) rather than adding a
dependency-ordering feature to `MergeBatch` itself. No `MergeBatch` schema
change is required by this design; a future implementation task may choose
to retire cross-Project `MergeBatch` grouping once per-repo queues cover its
use cases, but that is an implementation-time call, not a spec requirement
here.

## Cross-capability references (not modified by this change)

- **`context-fabric`** (proposed by `add-mission-cowork`, not yet in
  `openspec/specs/`): once landed, its provenance/`SourceRef` model needs a
  `RepositoryId` axis so Context Packs can cite which repository a fact or
  code reference came from, and its visibility filter needs to honor the
  attachment access levels this change defines. Left to whichever child
  change lands `context-fabric` against a tree that already has
  `repositories`.
- **`memory`** (`openspec/specs/memory/spec.md`): the repository memory
  layer (flow 7) needs a `repository` scope alongside today's
  `user | project | global` in `memory_facts`, and a promotion path mirroring
  `add-mission-cowork/specs/memory/spec.md`'s candidate/conflict/
  declassification states. Not modified in this change — the routing and
  ownership rules are specified here (flow 7); the schema/store change is
  deferred to a follow-up child change once this module exists to promote
  into.
- **merge queue** (no dedicated openspec capability exists today; the
  engine's `MergeQueueEntry`/`MergeBatch` are undocumented in `openspec/`):
  this design's "Relationship to issue #50" section is the normative
  statement of intent; a future change may formalize a `merge-queue`
  capability spec if one does not already exist by the time repo-scoping
  ships.

## Decisions

### 1. Repository is a standalone registered unit, not a Project child

**Choice:** `Repository` lives in its own module (`Comuki.Modules.Repositories`),
attached to Projects via `ProjectRepositoryAttachment`, mirroring how
`Comuki.Modules.Compute` and `Comuki.Modules.Projects` already sit as siblings
composed by the host.

**Why:** a Repository routinely outlives, and is shared across, more than one
Project (R1). Modeling it as a Project-owned child would force either
duplication (one Repository row per Project) or a parent/child reference
`Comuki.Modules.Repositories` cannot make back into `Comuki.Modules.Projects`
without breaking the modular-monolith law.

**Rejected:** Repository as a value object under Project (breaks R1's sharing
requirement); Repository as a field expansion of the existing scalar
`SourceGitUrl` (breaks R1, R3, R4, R6 simultaneously — no room for policy,
credentials, or links).

### 2. One merge queue per repository, not per Project

**Choice:** repo-scope the existing `MergeQueueEntry`/`MergeBatch` machinery
by `RepositoryId` rather than inventing a second queue concept.

**Why:** `MergeQueueEntry.ProjectId` is already nullable for "release train"
cross-Project scenarios — the queue was already drifting toward
repo-centric scoping before this change. Reusing it avoids two competing
merge mechanisms.

**Rejected:** a new `RepositoryMergeQueue` aggregate parallel to
`MergeQueueEntry` (duplicate mechanics for no behavioral gain).

### 3. Artifact readiness is adapter-based, not a generic polling engine

**Choice:** `ArtifactSource` is a small adapter interface
(`ResolveReadiness(VersionConstraint) -> ready | pending`) per link type/kind,
not a general-purpose scheduler.

**Why:** each kind (NuGet feed, npm feed, container registry, git tag,
merge-to-branch, CI status) has a genuinely different readiness signal and a
genuinely different webhook-or-poll story; a generic engine would either
under-fit all of them or become its own subsystem. R9 already enumerates the
closed adapter set.

**Rejected:** a generic "watch any URL for change" primitive (too broad,
no typed version-constraint semantics).
