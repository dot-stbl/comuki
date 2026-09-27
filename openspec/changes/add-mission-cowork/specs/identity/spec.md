## ADDED Requirements

### Requirement: Configurable role definitions
Permission keys and their object semantics SHALL remain stable contracts owned by capabilities. Except for the break-glass platform owner, roles SHALL be versioned self-hosted configuration that names a set of permissions and constraints. A grantor may include only permissions the grantor effectively holds, that are marked delegable, within a target scope no broader than the grantor's scope and policy ceiling. Authorized humans may create, rename, revise, deprecate, and grant roles through typed capabilities; changes preserve historical definitions used by past Decisions.

#### Scenario: Project defines developer role
- **WHEN** a project administrator publishes a `developer` role from allowed project permissions
- **THEN** the role can be granted without a platform release and audit records its definition version

#### Scenario: Role widens permissions
- **WHEN** a published role version adds a permission
- **THEN** existing grants do not receive it until explicitly regranted and approved

#### Scenario: Role tightens permissions
- **WHEN** a role removes a permission or narrows a constraint
- **THEN** the tightening applies immediately and subsequent authorization records the new effective version

### Requirement: Delegable permission ceiling
Each permission SHALL declare whether and within which scopes it is delegable. Creating a role or grant SHALL be limited to the intersection of the grantor's current effective permissions, delegable permissions, target scope no broader than the grantor's scope, and platform/project policy ceiling. Protected permissions use dedicated step-up or break-glass flows.

#### Scenario: Project administrator attempts platform grant
- **WHEN** a project-scoped administrator includes a platform-only or non-delegable permission in a custom role
- **THEN** validation rejects publication before any grant can use the role

### Requirement: Break-glass platform owner
The platform SHALL provide a minimal built-in `platform-owner` role that cannot be deleted, weakened, delegated to Brain, or satisfied by a service account. Its grant/recovery operations require step-up authentication, durable audit, and lockout prevention. Deployments SHALL always retain at least one recoverable human platform owner.

#### Scenario: Last platform owner revoked
- **WHEN** an operation would remove the final recoverable human platform owner
- **THEN** validation rejects the change before commit

### Requirement: Configurable Mission roles
Mission roles SHALL be versioned named bundles of Mission capabilities rather than fixed authorization authorities. Deployments/projects start with owner/editor/commenter defaults and MAY rename or extend them. The invariant that every live Mission has at least one active human with owner capability remains fixed; automation cannot satisfy it.

#### Scenario: Custom reviewer role
- **WHEN** a project defines a Mission role with read/comment/review but no invite or goal-edit capability
- **THEN** participants may be assigned that role and enforcement follows its pinned definition

### Requirement: Actor, credential, and authorization subject
The platform SHALL distinguish human actor id, optional credential id, authorization subject, and actor kind (`human`, `service-account`, `brain`, `worker`, `system`). Mission membership is evaluated for the actor; capability permissions are evaluated for the authorization subject; audit stores all applicable identities.

#### Scenario: Human uses scoped API key
- **WHEN** a Mission participant uses a CLI API key with insufficient project permission
- **THEN** membership succeeds but the capability is denied for the key authorization subject

### Requirement: Mission object access
Mission access SHALL be decided by one policy combining current project authorization, active Mission membership or audit grant, object visibility, credential restrictions, and requested action. The same decision is used by REST, SignalR, capability queries, retrieval, notifications, Tasks, Runs, and artifacts. Denial does not disclose object existence.

#### Scenario: Legacy Run route is mediated
- **WHEN** a project member requests a Run belonging to a private Mission without Mission access
- **THEN** the existing Run route answers not found

### Requirement: Distinct-human approvals
When capability policy requires multiple approvals, distinctness SHALL be calculated by human actor id. Two credentials, sessions, or service accounts controlled by the same human SHALL NOT satisfy a two-person rule. Every approval binds to action digest, policy version, relevant object versions, and expiry and is rechecked before effect.

#### Scenario: Same user approves twice
- **WHEN** a requester approves once by cookie and once by API key
- **THEN** only one distinct-human approval is counted

### Requirement: Brain control-plane hard deny
Changes to Brain autonomy maxima, capability exposure, approval policy, role/grant definitions, capability grants, runtime/bootstrap protocols, and plaintext secret reveal SHALL not execute autonomously through Brain. Brain MAY read redacted typed configuration and prepare role/grant diffs, but Identity policy requires explicit authorized human approval and step-up. Runtime/bootstrap, plaintext secret reveal, and Brain self-expansion remain unrepresentable.

#### Scenario: Brain proposes self-expansion
- **WHEN** Brain attempts to invoke a capability that increases its own access
- **THEN** the invocation is denied as unrepresentable regardless of Mission autonomy

### Requirement: Service account participation
Project administrators SHALL create service-account principals and grants. Mission owners or authorized editors MAY invite an eligible service account as an automation commenter/editor, never as human owner or human approver. Service messages display automation identity and owner/project. Brain invocation by automation requires a separate scoped grant, autonomy ceiling, rate, and budget.

#### Scenario: Automation cannot satisfy approval
- **WHEN** a policy requires one or two human approvals
- **THEN** approval from a service account does not count toward the requirement
