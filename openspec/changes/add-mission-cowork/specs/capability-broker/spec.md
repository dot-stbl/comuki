## Purpose

Defines one semantic command/query plane through which people, clients, Brain, automation, and internal adapters use Comuki capabilities under the same policy and audit rules.

## ADDED Requirements

### Requirement: Canonical semantic capability catalog
The platform SHALL expose versioned semantic commands and queries rather than treating raw HTTP endpoints as model tools. HTTP, dashboard, CLI, MCP, Brain, and automation projections SHALL resolve to the same capability contracts and observable behavior.

#### Scenario: Same command from chat and dashboard
- **WHEN** an authorized user creates a Task from dashboard or asks Brain to create the same Task
- **THEN** both paths apply the same validation, authorization, idempotency, policy, and audit behavior

### Requirement: Internal integration event compatibility
Internal integration events SHALL use stable lowercase type names ending in a major suffix, for example `work.task.resolved.v1`. Changes within one major event type are additive and tolerant. A breaking payload publishes a new major type; consumers SHALL support the old and new names during migration until old outbox, inbox, dead-letter, and projection watermarks are drained.

#### Scenario: Breaking event payload
- **WHEN** `work.task.resolved.v1` cannot evolve additively
- **THEN** the publisher emits `work.task.resolved.v2` only after consumers can process both and v1 is retired after its durable backlog drains

### Requirement: Actor-bound invocation
Every invocation SHALL bind human actor, optional credential, authorization subject, source, project/object scope, correlation, causation, capability version, and idempotency key on the server. Models SHALL NOT receive or choose credentials, authority scopes, or system identity.

#### Scenario: API key acts for a participant
- **WHEN** a user invokes a capability through an API key
- **THEN** membership is evaluated for the human actor, project permission for the key authorization subject, and audit records both ids

### Requirement: Exposure classes and hard deny
Capabilities SHALL be classified as operator, internal-allowlisted, runtime-protocol, or bootstrap/secret-reveal. Brain may receive operator capabilities and project-admin-enabled members of the platform allowlist. Runtime-protocol, bootstrap, plaintext secret reveal, and Brain control-plane mutation SHALL be unrepresentable to Brain regardless of project policy.

#### Scenario: Brain requests a protocol capability
- **WHEN** model output names worker heartbeat or plaintext secret reveal
- **THEN** the broker rejects it as unavailable without attempting execution

### Requirement: Four-level autonomy
Effective autonomy SHALL be one of `Observe`, `Suggest`, `ActSafe`, or `Autopilot`. Project policy defines the maximum; Mission policy selects a default at or below it; an authorized owner may grant a bounded time-limited increase up to the maximum; any participant may lower autonomy for one request. Policy controls approval counts and step-up for every Brain-exposable capability.

#### Scenario: Autonomy lowered during operation
- **WHEN** Mission autonomy is lowered while a Brain operation is reasoning
- **THEN** analysis may continue but every pending effect is re-evaluated and may become awaiting approval or denied

### Requirement: Context-owned policy
The bounded context that owns an action SHALL define its effect class, preconditions, risk inputs, approvals, executor, and compensation semantics. The broker supplies the shared invocation, authorization, autonomy, operation, idempotency, and audit mechanism; Mission SHALL NOT implement foreign domain actions.

#### Scenario: Compute scale intent
- **WHEN** Brain requests more execution capacity
- **THEN** Compute policy classifies and executes the intent through the broker while Mission stores only the deliberation references

### Requirement: Durable operation lifecycle
Mutating or long-running invocations SHALL produce a durable operation with stable states including proposed, awaiting approval, accepted, running, succeeded, failed, cancelled, denied, and expired. Approval binds to an immutable action digest and current guards. Replaying the same idempotency key and input returns the existing operation; different input conflicts.

#### Scenario: Retry after uncertain response
- **WHEN** a client retries a Task-create command after losing the response
- **THEN** the broker returns the original operation/result and no second Task is created

### Requirement: Lightweight query path
Ordinary read/list queries SHALL use the same catalog, actor binding, permission, and object policy but SHALL NOT create durable operation/idempotency rows by default. Long-running, asynchronous, expensive, or explicitly sensitive reads use the durable operation lifecycle and audit policy.

#### Scenario: List Missions
- **WHEN** an authorized client lists accessible Missions
- **THEN** the broker authorizes and returns the query with telemetry but creates no operation row

#### Scenario: Approval expires
- **WHEN** an AwaitingApproval operation remains unresolved for the default seven days
- **THEN** the operation expires, its proposal remains auditable, and execution requires a new proposal with current guards

### Requirement: Capability-specific safety floors
Context-owned policy MAY permit zero, one, or two approvals for Brain-exposable actions, subject to platform floors. Identity role/grant writes are proposal-only with at least one authorized human approval and step-up. Merge/deploy may be auto-executed only when environment gates, branch protection, verification, budget, and provider policy pass. Brain SHALL NOT raise its own operation/project budgets, but may explain the gap. Secret use exposes redacted metadata and opaque references; plaintext never enters model context.

#### Scenario: Brain assigns deployment secret
- **WHEN** Brain proposes a Task requiring an available secret reference
- **THEN** policy may bind the opaque reference under `secret:use` without revealing its value to Brain or room

### Requirement: Catalog pin and current policy
A long-running operation SHALL pin capability schemas and versions while re-checking current exposure, permissions, autonomy, policy, and object state before every effect. Revocation SHALL stop future effects even when the operation began earlier.

#### Scenario: Capability revoked mid-operation
- **WHEN** a project administrator disables an internal capability used by an active Brain operation
- **THEN** staged calls using it are denied or require re-planning; the old schema remains readable for audit
