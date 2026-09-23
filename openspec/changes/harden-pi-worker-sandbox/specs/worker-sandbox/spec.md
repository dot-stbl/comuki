## Purpose

Defines the fence around a `strong` pi worker: default-deny egress, allowlist intersection, fail-closed starts, execution conditions, and opt-in operator debug. This is the floor isolation class `strong` in cowork worker-pools; it does not describe warm slots.

## ADDED Requirements

### Requirement: Isolation class strong is the 1:1 container
The current worker (one container, one Translator, one `pi --no-session` process) SHALL be isolation class `strong`. A profile that omits an isolation class SHALL default to `strong`. The platform SHALL NOT silently run `strong` work as a process slot inside a shared host.

#### Scenario: Unspecified profile is strong
- **WHEN** a work item is claimed under a profile with no isolation class
- **THEN** the execution runs in its own container or Job and is labelled `strong`

### Requirement: Effective egress is profile intersect project
Effective egress SHALL be the intersection of the profile allowlist and the project allowlist. Brain, the work-item brief, and pi SHALL NOT add destinations. The default allowlist SHALL be the model proxy, the package registry (Nexus), and the host of the project's `SourceGitUrl`. A profile MAY only narrow that set, never widen it past the project.

#### Scenario: Brain cannot open a host
- **WHEN** a brief or Brain plan names an extra egress host
- **THEN** the provider does not add it and outbound connections to that host fail

#### Scenario: Default allowlist
- **WHEN** neither profile nor project lists extra hosts
- **THEN** the worker can reach the proxy, Nexus, and the SourceGitUrl host, and nothing else

### Requirement: Unfenced start is refused
If the provider cannot apply the effective egress fence, it SHALL NOT start the worker. The only exception is an explicit deployment flag `Compute:AllowUnfencedEgress=true`, which SHALL be treated as a development override and SHALL NOT be the production default.

#### Scenario: Production without a fence
- **WHEN** the Kubernetes provider cannot attach a network policy and the unfenced flag is unset
- **THEN** the worker is not started and the scale pass records a typed fence error

#### Scenario: Dev override
- **WHEN** `Compute:AllowUnfencedEgress` is true
- **THEN** the worker may start without a fence and the journal records that the execution is unfenced

### Requirement: Execution conditions are journaled
Each execution SHALL emit journal conditions `WorkspacePrepared`, `EgressApplied`, and `AgentRunning` on the bound run. `AgentRunning` SHALL become true only after the agent process has started. A container that is still in the claim loop is not `AgentRunning`.

#### Scenario: Warm container is not the agent
- **WHEN** a worker container is running but has not claimed a work item
- **THEN** the run (once claimed) does not show `AgentRunning` until pi has been spawned

#### Scenario: Prepare failure
- **WHEN** workspace preparation fails
- **THEN** `WorkspacePrepared` is false, pi is not started, and the work item is failed

### Requirement: Operator debug is opt-in
Interactive attach or exec into a worker container SHALL require an explicit debug flag on the host or run and SHALL default to off. Debug SHALL be an operator surface; the agent process SHALL NOT be given a guest process or filesystem API.

#### Scenario: Default refuses exec
- **WHEN** an operator calls exec on a worker whose debug flag is off
- **THEN** the call is refused

#### Scenario: Debug on
- **WHEN** debug is enabled for that worker
- **THEN** an operator with the matching permission may exec into the Translator container
