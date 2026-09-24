## Purpose

Defines durable, resumable Brain and subbrain operations that reason over bounded Context Packs, delegate research safely, and stage effects through the Capability Broker.

## ADDED Requirements

### Requirement: Durable Brain operation
Every non-trivial Brain request SHALL have an operation id, immutable input snapshot, Context Pack reference, budgets, capability catalog pins, progress, checkpoints, evidence, and terminal outcome. Project policy and request kind define budgets under deployment ceilings. Defaults are 5 minutes for chat, 15 minutes for completion review, 30 minutes for research, 16 total model calls, depth two, fan-out four, and at least 25% of the model window reserved for tools/synthesis/output. Self-hosted monetary cost is unlimited only when explicitly left unset; call/token/time limits still apply. Host or Brain restart SHALL resume without replaying committed effects.

#### Scenario: Restart after committed tool call
- **WHEN** Brain restarts after a capability effect commits but before final synthesis
- **THEN** the operation reads the committed call ledger and does not repeat the effect

#### Scenario: Budget exhausted
- **WHEN** an operation reaches its call, token, time, or configured cost limit
- **THEN** Brain returns a partial synthesis with evidence, gaps, and continuation options and does not execute effects lacking sufficient evidence

### Requirement: Concurrent operations and effect lanes
Multiple Brain operations MAY run concurrently in one Mission using independent snapshots. Reads and research may proceed in parallel; conflicting effects SHALL serialize through capability guards and proposal lanes. Chat remains usable while operations run.

#### Scenario: Two participants ask concurrently
- **WHEN** two participants start unrelated Brain analyses
- **THEN** both operations progress independently and any later conflicting mutations are re-evaluated before execution

### Requirement: Bounded subbrain delegation
The main Brain MAY create durable child operations in a budgeted acyclic delegation graph with platform-maximum depth two and default maximum fan-out four. Project policy MAY raise fan-out within a deployment ceiling but cannot raise depth. Each child SHALL receive a narrow Context Pack, source/capability grant, output contract, cost and time budget. A policy router chooses an allowed model from role, output, risk, and budget; model credentials and unapproved model ids are not model-controlled. Children SHALL NOT wait synchronously on ancestors or siblings and SHALL NOT directly commit mutations.

#### Scenario: Duplicate delegation collapsed
- **WHEN** two branches request the same role, normalized question, and source set
- **THEN** the coordinator reuses one child operation or evidence report

### Requirement: Worker-backed research
Brain and subbrains SHALL have no direct filesystem or process access. When code, files, tools, or isolated environments are required, they stage a bounded Orchestration `ExecutionRequest` with `BrainResearch` origin that executes as ordinary WorkItems under worker leases. Results return to the Brain operation with provenance and may create selective memory Candidates; no user-visible or hidden WorkTask is created.

#### Scenario: Subbrain inspects a repository
- **WHEN** a child operation needs repository evidence
- **THEN** a read-only research WorkItem runs in an isolated workspace and returns an evidence report to that child

### Requirement: Snapshot freshness and guards
Children reason against immutable snapshots. Before final synthesis and before every effect, the coordinator SHALL compare relevant authority watermarks and object versions. It may refresh authority sections or create a new operation revision; stale output cannot mutate state.

#### Scenario: Goal changes during analysis
- **WHEN** Mission criteria change while a completion operation is running
- **THEN** its old findings remain auditable but cannot close the Mission without a refreshed revision

### Requirement: Cooperative cancellation
Cancelling an operation SHALL prevent new delegations and effects, propagate cancellation to children and research WorkItems, preserve trace and partial evidence, and leave all staged mutations unexecuted. Read calls already completing may be recorded as partial evidence.

#### Scenario: User cancels long research
- **WHEN** an authorized participant cancels a running Brain operation
- **THEN** child work is stopped cooperatively and the room shows a cancelled operation with retained provenance

### Requirement: Synthesis by claims and evidence
Final synthesis SHALL merge claims with supporting and contradicting evidence, source authority, freshness, and confidence. Model agreement count SHALL NOT override authoritative state or accepted Decisions. Reserved synthesis budget cannot be consumed by child operations.

#### Scenario: Children disagree
- **WHEN** two subbrains return conflicting conclusions
- **THEN** the main Brain reports the conflict and its evidence instead of choosing by majority vote

### Requirement: Retrieval coverage and clarification
Every request kind SHALL define a coverage contract for must-have authority, evidence, and procedures. Context retrieval stops when coverage and quality thresholds are satisfied, expanding adaptively otherwise. Brain SHALL discover facts itself; it creates a clarification interrupt when alternative human answers would materially change intent, risk, policy choice, or irreversible effect.

#### Scenario: Ambiguous destructive request
- **WHEN** a user request has two plausible targets with different irreversible effects
- **THEN** Brain asks for clarification instead of choosing a target or spawning more research

### Requirement: Diagnostic reasoning capture
Normal operation SHALL store progress, tool/delegation trace, evidence, concise rationale, Decisions, and usage, not hidden chain-of-thought. Full reasoning capture MAY be enabled only by deployment configuration for bounded diagnostics with explicit retention, audit, and sensitive-data handling.

#### Scenario: Normal production operation
- **WHEN** Brain completes under ordinary configuration
- **THEN** participants can inspect rationale and evidence but no raw hidden reasoning transcript is retained

### Requirement: Authenticated Brain execution session
The separate Brain runtime SHALL receive a short-lived opaque execution session bound to invocation id, expiry, Context Pack handle, and filtered capability-catalog hash. It SHALL NOT receive user cookies, API keys, permission snapshots as bearer authority, or system credentials. Remote Host-to-Brain gRPC SHALL use mutual TLS; a colocated deployment MAY use loopback or Unix-socket transport plus the opaque session. Every staged effect returns to Host for current Broker authorization and policy.

#### Scenario: Brain requests a capability effect
- **WHEN** Brain emits a tool intent during a valid execution session
- **THEN** Host resolves the session and re-authorizes the effect without exposing the requester's credential to Brain
