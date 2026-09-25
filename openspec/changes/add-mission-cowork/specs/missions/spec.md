## Purpose

Defines private project-scoped Missions where any number of participants and the Brain collaborate around a shared outcome, Tasks, evidence, and decisions.

## ADDED Requirements

### Requirement: Mission lifecycle
A Mission SHALL belong to exactly one home Project and MAY list additional participating Projects (see "Home project and participating projects" below), and SHALL follow `Draft → Active → Review → Completed`, with `Cancelled` terminal from any non-terminal state. Completed SHALL reopen only through an approved new revision and reopen Decision. Progress is derived from linked Tasks, while Mission status records the lifecycle of the shared goal.

#### Scenario: Reopen completed goal
- **WHEN** an authorized proposal adds a new goal revision to a Completed Mission
- **THEN** approval creates a new evidence generation and returns the Mission to Active without erasing the prior completion

### Requirement: Home project and participating projects
A Mission SHALL have exactly one home Project, the default budget and memory owner. A Mission MAY additionally list participating Projects; the list SHALL default empty, in which case every existing Mission behavior is exactly the single-Project case unchanged. A participating Project SHALL be added only through an approved capability-broker invitation initiated by the home Project's Brain when work needs a repository the home Project has no write access to (full invitation/approval/budget mechanics are specified by the `repositories` capability's Mission-participation routing, added in `add-multi-repo-projects`). Work on a given repository SHALL resolve through whichever Project — home or participating — holds a write attachment to it; absent one, the work is external-blocked rather than proceeding under any Project's authority.

#### Scenario: Default Mission has no participants
- **WHEN** a Mission is created without inviting any participating Project
- **THEN** it behaves exactly as a single-Project Mission — every prior scenario in this capability holds unchanged

#### Scenario: Home project without write access invites a participant
- **WHEN** a Mission's home Project has no write attachment to a repository the work targets, and a participating Project does
- **THEN** the home Project's Brain invites the participating Project through the capability broker, and the participating Project's approval is required before its budget is used

### Requirement: Arbitrary participant membership
A Mission SHALL support any number of human and service-account participants. Room roles are configurable capability bundles initialized with owner/editor/commenter defaults. Every non-terminal Mission SHALL have at least one active human with owner capability; service accounts are a distinct actor kind and SHALL NOT count as owners or human approvals. No product contract may assume exactly two participants.

#### Scenario: More than two collaborators
- **WHEN** five users accept invitations to one Mission
- **THEN** all five can concurrently receive the same room stream subject to their roles

#### Scenario: All owners disabled
- **WHEN** every human owner is disabled or deleted
- **THEN** the Mission becomes Orphaned for consequential Decisions and project-admin recovery assigns a new owner through an audited flow

### Requirement: Private membership access
Mission content SHALL be visible only to active participants and time-bounded audit grants. Project membership alone SHALL NOT reveal a Mission, its Tasks, Runs, artifacts, search hits, citations, notifications, or existence; denied object access answers as not found.

#### Scenario: Project member is not a participant
- **WHEN** a project member requests a private Mission id without membership
- **THEN** the response is not found and no related data is disclosed through another surface

### Requirement: Invitations and audit access
Invitations SHALL follow `Pending`, `Accepted`, `Rejected`, `Revoked`, or `Expired`, carry inviter, role, reason, and TTL, and expose only a safe preview before acceptance. Mentioning a non-participant creates an invitation request. A project administrator MAY open a short read-only audit session after step-up authentication and a mandatory reason; participants are notified and the access is durably recorded.

#### Scenario: Mention does not leak room content
- **WHEN** a participant mentions a project user who is not in the Mission
- **THEN** the user receives a safe invitation preview but no room messages or artifacts

### Requirement: Versioned goal and Mission Task links
Mission goals and acceptance criteria SHALL be immutable revisions. A Mission-owned Task link SHALL carry required/optional classification, ordering, role in the goal, local criteria, and expected evidence without changing the standalone Task aggregate. Editors may propose revisions; owners or authorized approvers decide them.

#### Scenario: Required is Mission-specific
- **WHEN** a standalone Task is attached as optional to one Mission
- **THEN** the Task itself does not acquire a global optional flag

### Requirement: Mission creation, activation, and impact
A manually created Mission SHALL start Draft. Draft activation requires a non-empty goal and at least one human owner; acceptance criteria are optional, but completion review requires at least one criterion or an explicit approved no-criteria Decision. Promoting an Active standalone Task creates an Active Mission. Goal revision, cancellation, completion, and deletion SHALL include an explicit per-Task impact map for active or optional work.

#### Scenario: Goal revision affects active Tasks
- **WHEN** a new goal revision is proposed while Tasks are active
- **THEN** the proposal declares keep, cancel-replace, block, or review for each affected Task and no Run changes silently

### Requirement: Mission completion evidence
A Run success SHALL not necessarily resolve its Task. Each Task/link SHALL pin a completion policy and expected evidence bounded by its profile: declared deterministic auto-verification, explicit human reviewers, or Brain-assisted review plus deterministic evidence. Failed deterministic verification blocks the Task and may produce a repair/waiver proposal; Brain cannot override the verifier. Required Failed outcomes require explicit waiver, replacement, or revision before completion. Waivers bind to Mission revision and Task-link version.

#### Scenario: Successful Run fails verification
- **WHEN** a Run succeeds but its declared evidence contract fails
- **THEN** the Run remains Succeeded, the Task becomes Blocked, and repair/waiver/revision requires a Decision

### Requirement: Durable room stream
A Mission SHALL expose one durable append-only stream for messages, Brain outputs, proposals, Decisions, Task/Run milestones, artifacts, invitations, and reviews. The server SHALL assign a gapless committed sequence after idempotency deduplication. Client message ids are UUIDv7; reuse with different content returns conflict. Edits create versions, deletions create tombstones, and citations bind to immutable message versions.

#### Scenario: Concurrent messages have one order
- **WHEN** several participants send messages concurrently
- **THEN** every client observes the same committed sequence after REST catch-up

### Requirement: Proposals and Decisions
Consequential changes SHALL be represented by addressable typed proposals and immutable Decisions. The actor, requester, approvers, capability version, action digest, relevant object versions, risk policy, citations, outcome, and rationale SHALL be auditable. Conflicting proposal lanes execute serially; later proposals are re-evaluated rather than winning by recency.

#### Scenario: Permission revoked before execution
- **WHEN** a proposal was approved but a required requester or approver permission is revoked before its effect
- **THEN** execution is denied or returned to approval after current policy is re-evaluated

### Requirement: Completion review
The platform SHALL enqueue one coalesced Brain review after all required Mission Task links are resolved. The review compares the current goal and criteria with Decisions and evidence and proposes completion, questions, or additional Tasks. Zero required Tasks do not auto-review. Optional active work is resolved explicitly in the completion proposal. Owners or authorized approvers decide completion.

#### Scenario: Missing work returns Mission to Active
- **WHEN** a completion review identifies an unmet criterion and its proposed Task is approved
- **THEN** the Mission remains or returns Active and the new Task link participates in the next evidence generation

#### Scenario: Review disagreement
- **WHEN** an owner or approver rejects completion with rationale
- **THEN** the proposal remains unresolved/superseded, Mission does not close by majority vote, and discussion or a new revision may trigger another review

### Requirement: Workspace and room projections
A Mission SHALL have two equal projections: a tracker-like Workspace and a shared Room. Workspace defaults to a Task board with dependency graph/table/timeline alternatives and exposes goal, criteria, progress, Decisions, and artifacts. Room appears as a distinct Mission conversation beside personal chats and uses the same durable stream. Creating a Mission is available from Workspace, personal chat capability, and Task promotion.

#### Scenario: Open Mission in CLI
- **WHEN** a participant runs `comuki mission <id>`
- **THEN** the shared Room opens with Mission header, participants, attention, linked Task summary, and the participant's unread catch-up
