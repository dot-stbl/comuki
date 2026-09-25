## Purpose

Defines the Repository registry: a standalone unit Projects attach to
(many-to-many), its repo-owned rules and credentials, the global repo-to-repo
link graph with artifact-based readiness, the cross-repo DAG and external-block
flows that use that graph, and the repository-scoped memory and Brain
visibility rules that follow from sharing one repository across Projects.

## ADDED Requirements

### Requirement: Repository is a standalone registered unit
The platform SHALL register a Repository (R1) as its own aggregate — url,
host, default branch — independent of any Project. A Repository SHALL be
attachable to more than one Project simultaneously; registering the same
`(host, url)` pair twice SHALL resolve to the existing Repository row rather
than creating a duplicate.

#### Scenario: Shared library attaches to two Projects
- **WHEN** a Repository already attached to Project A is attached to Project B
- **THEN** one Repository row serves both attachments and neither Project's
  attachment mutates the other's

### Requirement: Project↔Repository attachment with role and access
A Project SHALL attach a Repository through a `ProjectRepositoryAttachment`
(R1) carrying a role (`primary` | `service` | `frontend` | `library` |
`deploy-gitops` | `docs` | ...) and an access level (`write` | `read` |
`external`). Effective access SHALL be `min(attachment.access,
credential.DefaultAccess)`; an attachment MAY declare a per-attachment
credential override in place of the repository's default credential (R4).

#### Scenario: Read attachment cannot escalate via a write-capable credential
- **WHEN** a Project's attachment declares `access: read` on a Repository
  whose credential defaults to `write`
- **THEN** the effective access for that Project's work on the Repository is
  `read`

#### Scenario: Attachment-level credential override
- **WHEN** an attachment declares a credential override distinct from the
  Repository's default credential
- **THEN** work initiated through that attachment resolves the override
  credential, not the Repository default

### Requirement: Repository owns its rules and its one merge queue
Protected branches, required checks, approvers, and merge-queue membership
SHALL be owned by the Repository (R3), never duplicated or overridden
per-Project. Exactly one merge queue SHALL exist per Repository; a Project
that initiates work on a Repository SHALL NOT bypass or reorder that
Repository's queue. A Project's role in a merge is limited to initiating
work, paying its budget, and supplying memory/context.

#### Scenario: Two Projects, one merge queue
- **WHEN** Project A and Project B both have write attachments to the same
  Repository and both submit changes
- **THEN** both changes enter the same single per-Repository merge queue and
  merge order follows that queue's rules, not either Project's

### Requirement: Credential ownership and access resolution
Credentials (deploy token, GitHub App installation, GitLab integration) SHALL
belong to the Repository (R4), one credential reference per Repository by
default. `ProjectRepositoryAttachment.Access` SHALL cap what an attachment can
do with that credential; the platform SHALL NEVER resolve an effective access
higher than both the attachment's declared access and the credential's
default access.

#### Scenario: External attachment never resolves a credential
- **WHEN** an attachment's access is `external`
- **THEN** no write or read credential is resolved for work initiated through
  that attachment — see "External block" below

### Requirement: Repository-to-repository link graph
The platform SHALL model repo-to-repo relationships as `RepositoryLink`s (R6)
— never as a Project-to-Project link — with a type from `package-pin` |
`submodule` | `api-contract` | `deploy-gitops` | `codegen-consumer` |
`read-context` | `fork-upstream`, a declared handoff artifact per type
(package version, commit SHA / submodule pointer, contract version, image
tag, generated code, or upstream merge respectively), and a version
constraint. A cross-product link (repos attached to different Projects) SHALL
use the same `RepositoryLink` shape as a same-Project link — no separate
ProjectLink concept SHALL exist.

#### Scenario: Cross-project link uses the same shape
- **WHEN** Repository X (Project A) declares a `package-pin` link to
  Repository Y (Project B)
- **THEN** the link is one `RepositoryLink` row identical in shape to a link
  between two repositories both attached to Project A

### Requirement: Link auto-discovery and human confirmation
The platform SHALL scan attached repositories on attach and periodically
(R10) for `.gitmodules`, `PackageReference`/`package.json`, OpenAPI client
configuration, helm/kustomize values, and fork remotes, writing discovered
edges as `RepositoryLink` with `status: suggested`. A `suggested` link SHALL
be a Brain hint only — it SHALL NOT drive planning or gate a cross-repo DAG
task until a human sets `status: confirmed`. When a previously confirmed
link's manifest evidence disappears on a later scan, the platform SHALL emit
a drift notification and SHALL NOT silently revert the link to `suggested`.

#### Scenario: Suggested link does not gate a DAG task
- **WHEN** discovery finds a new `package-pin` edge and writes it `suggested`
- **THEN** no cross-repo DAG task waits on that edge's artifact until a human
  confirms it

#### Scenario: Drift on a confirmed link notifies rather than reverts
- **WHEN** a confirmed link's manifest evidence is absent on the next scan
- **THEN** the link stays `confirmed` and a drift notification is recorded

### Requirement: Cross-repo DAG execution with artifact wait
A cross-repo change SHALL decompose into a DAG of tasks where each task
claims exactly one target Repository read-write plus its confirmed-link
graph neighbours read-only, capped at a configured neighbour limit (R8). A
downstream task whose target depends on a confirmed link SHALL wait until
that link's `ArtifactSource` adapter reports the link's version constraint
satisfied (R9) before it may start; the platform SHALL use the adapter's
webhook when available and SHALL otherwise poll. A submodule-pointer bump
SHALL be produced as a mechanical step with no worker spawned.

#### Scenario: Downstream waits for upstream artifact
- **WHEN** a downstream task's target repository has a confirmed
  `package-pin` link requiring `>= 2.3.0` and the upstream repository has
  only published `2.2.0`
- **THEN** the downstream task remains waiting and does not start until an
  `ArtifactSource` read reports `>= 2.3.0` satisfied

#### Scenario: Submodule bump has no worker
- **WHEN** an upstream repository's merge satisfies a confirmed `submodule`
  link
- **THEN** the downstream pointer commit is produced directly, without
  claiming a worker task

### Requirement: External block when the Project has no write access
When a task needs to change a Repository the initiating Project's attachment
resolves to `read` or `external` effective access, the platform SHALL NOT
edit that Repository (R2, R5). It SHALL instead create an
`ExternalChangeRequest` carrying a Brain-authored capability specification,
sent through the Repository's configured request channel — an issue in its
tracker when an integration exists, a draft for a human to forward otherwise,
or a pull request from a fork when read access permits opening one. The
originating task's status SHALL become `blocked-external`. An artifact
watcher SHALL observe the expected version/tag/merge signal, and the Brain
SHALL verify the resulting capability actually satisfies the original
specification before the request moves to `verified` and the task unblocks.

#### Scenario: No write access routes to a request, never an edit
- **WHEN** a task targets a Repository where the initiating Project's
  effective access is `external`
- **THEN** no write is attempted against that Repository and an
  `ExternalChangeRequest` is created instead

#### Scenario: Unblock requires verification, not just a signal
- **WHEN** the watched artifact fires (a merge lands) but the Brain
  determines the merged change does not satisfy the requested capability
- **THEN** the `ExternalChangeRequest` stays `awaiting-artifact` and the
  originating task remains `blocked-external`

### Requirement: Backward-compatible upstream changes and bounded rollback
An upstream repository change consumed by a downstream link SHALL pass a
contract-compatibility check before merging (R13). When a downstream task
fails after an upstream merge, the downstream task SHALL go back for rework;
the upstream merge SHALL NOT be touched by that failure. A revert of the
upstream change SHALL only be proposed automatically when the upstream
change is confirmed to have broken other consumers, and SHALL only merge on
explicit approval (revert-PR).

#### Scenario: Downstream failure does not touch upstream
- **WHEN** a downstream task fails after consuming a compatible upstream
  artifact
- **THEN** the downstream task is reworked and the upstream repository's
  merged change is untouched

#### Scenario: Revert requires approval
- **WHEN** an upstream change is confirmed to have broken another consumer
- **THEN** a revert-PR is proposed automatically and merges only after
  explicit approval

### Requirement: Multi-project Mission participation routing
Work on Repository R initiated from a Mission SHALL first try the Mission's
home Project's write attachment to R (R7). When the home Project has no write
attachment to R, the Brain SHALL invite a participating Project that holds a
write attachment to R; that Project's own approval SHALL be required, routed
through the capability broker, and the work SHALL run on the invited
Project's own budget. When no home-or-participating Project holds a write
attachment to R, the "External block" requirement above applies. The merge
SHALL always follow R's own repository rules regardless of which Project's
budget funded the work.

#### Scenario: Home project lacks write access, participant invited
- **WHEN** a Mission's home Project has only a `read` attachment to R and a
  participating Project has a `write` attachment to R
- **THEN** the Brain invites the participating Project through the
  capability broker and the work runs on that Project's budget once approved

#### Scenario: No attached-or-participating Project has write access
- **WHEN** neither the home Project nor any participating Project holds a
  write attachment to R
- **THEN** the work follows "External block" rather than proceeding under
  any Project's budget

### Requirement: Repository memory layer
The platform SHALL maintain a repository-scoped memory layer alongside
existing scopes (R11), holding only facts derived from the repository itself
and incidents mapped to it via its deploy-gitops link. Repository memory
SHALL be shared by every Project attaching that repository. Project-
originated context (tickets, customers, business decisions) SHALL NOT enter
repository memory directly; promotion from Project memory to repository
memory SHALL require a redacted, provenance-carrying proposal naming every
Project attaching the repository as the disclosure audience, approved under
both the source Project's and the repository's policy (R12).

#### Scenario: Incident maps to repository memory via deploy link
- **WHEN** a background watcher observes an incident on a service backed by
  a repository through its deploy-gitops link
- **THEN** the incident is written to that repository's memory, not to the
  observing Project's memory

#### Scenario: Project context cannot enter repository memory unredacted
- **WHEN** a Project's ticket-derived context is proposed for repository
  memory without a redaction/provenance proposal
- **THEN** the write is refused

### Requirement: Brain visibility across the repository graph
Within a Project, the Brain SHALL see that Project's attached repositories
fully (code and memory, per the attachment's access). For a graph neighbour
reached via a confirmed `RepositoryLink`, the Brain SHALL always see its
metadata — name, link type, artifact/version, public contract surface, and
owning Projects (R16) — regardless of the initiating Project's access to
that neighbour. The Brain SHALL see a neighbour's code or memory only when
the initiating attachment carries at least `read` access to that neighbour.

#### Scenario: Metadata visible without code access
- **WHEN** Project A's Brain inspects a graph neighbour Repository that A has
  no attachment to at all
- **THEN** the Brain sees the neighbour's name, link type, artifact/version
  and owning Projects, but not its code or memory

#### Scenario: Code visible only with read access
- **WHEN** Project A holds a `read` attachment to a graph neighbour
  Repository
- **THEN** the Brain may read that neighbour's code and memory when
  reasoning about A's work, still bounded by A's `read` (never `write`)
  access
