## Purpose

Defines durable user-facing Tasks independently from intake envelopes, collaborative Missions, and the technical Runs and WorkItems that execute them.

## ADDED Requirements

### Requirement: Standalone Task aggregate
The platform SHALL represent admitted work as a Task with a project, title, versioned brief, one or more source references, lifecycle status, responsible actors, and optional Mission association. A Task SHALL exist without a Mission and SHALL belong to at most one Mission. Standalone visibility follows Project RBAC; responsible assignment affects coordination and attention, not authorization.

#### Scenario: Tracker ticket becomes a Task
- **WHEN** an admitted tracker ticket is claimed
- **THEN** the platform creates one Task retaining the tracker source reference before dispatching execution

### Requirement: Primary and related sources
A Task MAY aggregate several tracker/native source references and SHALL designate at most one primary source for full supported lifecycle sync. Changing primary source requires a Decision; the previous primary remains related and receives a link note. Related sources receive accepted key Decisions and terminal summaries without intermediate status spam.

#### Scenario: Change primary tracker
- **WHEN** an authorized Decision changes the primary source
- **THEN** future lifecycle sync targets the new primary while history and related-source linkage remain auditable

### Requirement: Task lifecycle and resolution
A Task SHALL follow `Draft → Ready → Active`, MAY enter `Blocked`, and SHALL terminate as `Resolved` or `Cancelled`. A resolved Task SHALL carry exactly one outcome: `Succeeded`, `Waived`, `Replaced`, or `Failed`. Exhausting Run attempts SHALL move an Active Task to Blocked for an explicit retry, replacement, waiver, or failed-resolution Decision; it SHALL NOT resolve automatically.

#### Scenario: Attempts exhausted
- **WHEN** the active Run fails and the retry policy has no remaining attempt
- **THEN** the Task becomes Blocked and awaits an explicit Decision

### Requirement: Sequential Run attempts
Each Task SHALL own an ordered sequence of Runs and SHALL have at most one active Run. A user-visible retry or replacement SHALL create a new Run attempt; a terminal Run SHALL NOT be resurrected. Infrastructure claim retries within one Run remain separate from Task attempt numbering.

#### Scenario: Replacement creates a new attempt
- **WHEN** authorized feedback replaces an active Run
- **THEN** the predecessor is durably fenced before a new Run with the next attempt ordinal becomes active

### Requirement: Task relations
Tasks SHALL support acyclic directed `blocks` relations and symmetric informational `relates-to` relations. A blocked prerequisite is satisfied by `Succeeded`; `Waived` and `Replaced` satisfy or transfer each affected edge only as stated by their Decision; `Failed` SHALL NOT satisfy an edge automatically. An optional Task MAY block a required Mission Task when the edge is explicit.

#### Scenario: Failed prerequisite remains blocking
- **WHEN** prerequisite Task A resolves Failed and blocks Task B
- **THEN** Task B remains Blocked until a later Decision changes the relationship or resolution

### Requirement: Cross-Mission dependencies
An explicit Task dependency MAY cross Missions in the same Project only when its creator can access both. A participant lacking access to the other Mission SHALL see a redacted external-dependency stub with status class and request-access/contact action, not title, content, or artifacts.

#### Scenario: Private upstream dependency
- **WHEN** Task B depends on Task A in an inaccessible Mission
- **THEN** B shows a redacted blocking dependency without revealing A's private details

### Requirement: Task dispatch policy
A ready Task SHALL be dispatched according to the effective project and Mission policy. Auto-dispatch SHALL remain bounded by authorization, autonomy, risk, budget, dependencies, and compute limits; otherwise the Task waits for an explicit dispatch Decision.

#### Scenario: Manual policy holds ready work
- **WHEN** a Task becomes Ready under manual dispatch policy
- **THEN** no Run is created until an authorized dispatch is approved

### Requirement: Task visibility follows Mission attachment
A standalone Task uses project object policy. On first attachment to a private Mission, the Task and all prior and future Runs and artifacts SHALL immediately require Mission access. Initial standalone-to-Mission attachment is allowed after prior Runs; transfer from one Mission to another is forbidden after the first Run.

#### Scenario: Prior artifact becomes private
- **WHEN** a standalone Task with an existing artifact is promoted into a Mission
- **THEN** a project member who is not a Mission participant can no longer read that artifact
