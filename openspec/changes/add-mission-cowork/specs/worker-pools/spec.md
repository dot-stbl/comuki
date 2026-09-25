## Purpose

Defines warm capability-advertising worker hosts with isolated concurrent execution slots and deterministic placement under project, provider, budget, and resource limits.

## ADDED Requirements

### Requirement: Worker host and execution slot identity
The platform SHALL distinguish WorkerHost, Slot, Execution, and AgentSession identities. A warm host MAY run multiple independent slots; each slot owns at most one active execution with its own workspace, credentials, lease, stream, heartbeat, cancellation, and cleanup.

#### Scenario: Parallel slots do not replace streams
- **WHEN** one host starts twenty slot executions
- **THEN** each maintains an independent command/event stream and cannot heartbeat or complete another slot's WorkItem

### Requirement: Advertised capabilities and hard capacity
A worker host SHALL advertise compatible profiles, images, tools, models, resource shape, and provider hard maximum slots. A provider/profile that does not declare safe concurrency defaults to one slot. Project quotas and provider limits bound active slots. Profile resource policy MAY permit measured oversubscription for I/O-heavy work within provider hard limits. The deterministic capacity planner chooses host count, placement, and active slot count; Brain cannot exceed these limits.

#### Scenario: Desired parallelism exceeds capacity
- **WHEN** Brain requests twenty slots but provider and project policy allow eight
- **THEN** the planner activates at most eight and reports the constrained plan and expected delay

### Requirement: Desired parallelism intent
Brain and operators MAY submit desired parallelism, priority, deadline, and cost preference as a capacity intent. Effective autonomy and Compute risk policy decide whether it executes automatically or becomes a proposal. The intent SHALL NOT name privileged provider credentials or bypass placement.

#### Scenario: Autopilot scales within quota
- **WHEN** Autopilot requests additional compatible slots within preapproved budget and quota
- **THEN** the planner may start or resize warm hosts without manual approval

### Requirement: Slot workspace isolation
Concurrent slots SHALL use separate mutable working directories, checkouts/worktrees, temporary files, context handles, execution-only credentials, agent processes, logs, and cleanup through a provider-selected workspace adapter. Projects within one self-hosted deployment are in one trusted deployment zone by default, so a warm container, Pod, or runner MAY host process slots from several Projects. Every Execution starts a fresh agent session and mutable workspace, model context, and credentials SHALL NOT cross slots.

#### Scenario: Two Tasks edit the same repository
- **WHEN** two slots execute against the same source repository
- **THEN** each works in an independent workspace and neither observes the other's uncommitted files

### Requirement: Isolation classes
Profiles and Project policy SHALL declare a minimum isolation class: `trusted-process` or `strong`. `trusted-process` uses separate processes, workspaces, leases, and credentials inside a warm host. `strong` uses a provider execution sandbox: sibling OCI container for Docker/Podman, separate Pod/Job for Kubernetes, or another advertised equivalent. A runner without a strong driver SHALL NOT claim strong work. Unknown profiles use the deployment's explicit default and SHALL NOT silently downgrade a strong request.

#### Scenario: Strong profile on Kubernetes
- **WHEN** a profile requires strong isolation
- **THEN** the planner creates a separate execution Pod/Job rather than another process in the multi-slot host Pod

### Requirement: Slot egress and quarantine
Effective network egress SHALL be the intersection of profile requirements and Project policy and SHALL be enforced by the provider; Brain cannot add arbitrary destinations. Suspicion of slot compromise quarantines the whole host: stop new claims, fence and stop all slots, rotate host credentials, preserve allowed evidence, and requeue independently by policy.

#### Scenario: One slot is compromised
- **WHEN** security policy flags one slot as compromised
- **THEN** the host is quarantined and no sibling execution can continue on that host

### Requirement: Host failure recovery
Every slot SHALL hold an independent lease and fencing generation. If a host fails, each affected execution independently expires and follows WorkItem retry policy; replacement hosts may claim requeued items. Failure of one host does not create a shared Task outcome for all slots.

#### Scenario: Warm host crashes
- **WHEN** a host with twenty running slots loses heartbeat
- **THEN** the reaper handles twenty independently fenced WorkItems and no late process may complete them authoritatively
