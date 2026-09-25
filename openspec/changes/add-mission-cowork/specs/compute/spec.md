## MODIFIED Requirements

### Requirement: Scale policy v0 (create-per-task)
Scale policy SHALL plan execution slots rather than assume one worker container per queued item. Inputs include compatible queued WorkItems, idle compatible slots, active slots, provider-advertised host limits, project quotas, desired-parallelism intents, budgets, priorities, and deadlines. The deterministic planner returns host start/stop and slot activation decisions and SHALL never exceed hard limits.

#### Scenario: Brain requests more than hard maximum
- **WHEN** desired parallelism is 20 and effective provider/project capacity is 8
- **THEN** the plan uses at most 8 slots and exposes the capacity constraint

#### Scenario: Backlog beyond cap
- **WHEN** 10 compatible items are queued, no slots are idle, and effective free capacity is 3
- **THEN** the policy activates or starts 3 slots, not 10

#### Scenario: Never reap below the warm floor
- **WHEN** two hosts are stale and the configured compatible warm floor requires one
- **THEN** the policy drains at most one host

### Requirement: Scale supervisor pass
The supervisor SHALL reconcile worker hosts and their advertised slots with providers, apply the capacity plan, start or resize compatible warm hosts, and drain stale idle hosts without interrupting active slots. A failed pass is retried idempotently. Brain may submit capacity intents but SHALL NOT directly start provider resources.

#### Scenario: Warm host reused
- **WHEN** a compatible host has four idle slots and four matching WorkItems queue
- **THEN** the supervisor assigns those slots before starting another host

#### Scenario: Idle worker reaped after TTL
- **WHEN** a fully idle host exceeds idle TTL and the warm floor allows removal
- **THEN** the supervisor drains and stops the host and revokes its host credential

#### Scenario: Adopted workers get a full TTL
- **WHEN** the orchestrator restarts and adopts a provider host it no longer knows
- **THEN** the host receives a fresh observed-activity timestamp before idle reaping

### Requirement: Per-project scale settings port
Per-project settings SHALL expose host and slot quotas, idle lifetime, cost bounds, allowed provider/resource classes, and optional profile/image/model constraints. Mission autonomy may request values only within project maxima; exceeding a budget, quota, or allowed provider follows Compute capability policy and approval.

#### Scenario: Project quota changes
- **WHEN** a project administrator lowers maximum active slots
- **THEN** new claims respect the lower limit and excess active executions drain according to policy rather than being silently killed

#### Scenario: Host swaps the store
- **WHEN** the host registers the Projects settings-backed adapter
- **THEN** capacity decisions observe settings writes without a restart

### Requirement: Kubernetes Job contract
The Kubernetes provider SHALL support the declared worker-host topology rather than require one Job per WorkItem. Each host resource advertises slot capacity and capabilities; slot executions remain independently leased even when they share a pod. Provider implementations MAY use different resource primitives as long as observable host/slot, fencing, isolation, and recovery semantics remain equivalent.

#### Scenario: Pod hosts several slots
- **WHEN** a Kubernetes worker host advertises eight compatible slots
- **THEN** up to eight independent executions may run in that host while each retains its own lease and workspace

#### Scenario: Job name uniqueness for same-ms UUID7
- **WHEN** two Kubernetes worker hosts are minted in the same millisecond
- **THEN** their provider resource names remain unique

#### Scenario: Failed Job is not retried by Kubernetes
- **WHEN** a worker-host Job exits non-zero
- **THEN** Kubernetes does not duplicate slot executions and orchestrator lease recovery decides their retries
