## Purpose

Defines the single SlotAdmission record that must succeed before a coding-agent container starts or pi spawns, composing environment class, isolation, secrets, edition, and profile so those gates cannot be skipped independently.

## ADDED Requirements

### Requirement: SlotAdmission is the only start input
Every coding-agent execution SHALL have a SlotAdmission record before `StartAsync` and before the Translator spawns pi. The record SHALL carry: target repository id, profile key, env class, isolation class (`trusted-process` | `strong`), secret refs (possibly empty), edition decision, profiles ref, and fleet publisher allowlist result. A start request that does not name a successful admission id SHALL fail. Until worker-pools land, the record still wraps the 1:1 container.

#### Scenario: Start without admission fails
- **WHEN** compute is asked to start a worker with profile and image but no admission id
- **THEN** start fails and no container is created

#### Scenario: Pi does not spawn on denied admission
- **WHEN** admission is denied after claim
- **THEN** the Translator does not start pi and the work item is failed with an admission code

### Requirement: Ordered typed evaluation
Admission SHALL evaluate in this order, stopping at the first deny: (1) confirmed env class on the target repository, (2) fleet publisher allowlist, (3) advertised capacity for that class, (4) isolation class the host can honor, (5) edition coverage for the class/runtime (Windows/GPU/community as paid keys when registered), (6) secret refs resolvable under the worker subject. Each deny SHALL use a stable code: `admission.env_unconfirmed`, `admission.publisher`, `admission.capacity`, `admission.sandbox`, `admission.edition`, `admission.secrets`. Missing GPU/Windows capacity SHALL be `admission.capacity`, never `blocked-external`.

#### Scenario: Unconfirmed class denies before capacity
- **WHEN** the target repository has no `EnvClass`
- **THEN** the code is `admission.env_unconfirmed` and no pool is consulted

#### Scenario: No Windows pool is capacity
- **WHEN** class `ue5.4-win` is confirmed and the fleet advertises zero slots of that class
- **THEN** the code is `admission.capacity`

### Requirement: Four behavior axes stay distinct
Admission SHALL treat four axes as distinct fields, never collapsed into the worker image: **role** (control-plane profile), **toolchain** (env class / `.comuki/environment.toml`), **product rules** (target repository git at the work ref, including AGENTS.md and analyzers), **fleet** (catalog allowlist, isolation, node pool). A profile SHALL NOT encode a compiler. An env class SHALL NOT encode a review vs implement role.

#### Scenario: Same class, two profiles
- **WHEN** `implement` and `pr-review` both target a repository bound to `net10-sdk-bun`
- **THEN** both admissions share env class and differ only in profile

#### Scenario: Profile does not name a compiler
- **WHEN** a control-plane profile body mentions `dotnet`
- **THEN** admission still takes env class from the repository binding, not from the profile text

### Requirement: Journal admission
A successful admission SHALL append `worker.admitted` with class, profile, isolation, and admission id (no secret values). A denial SHALL append `worker.admission_denied` with the typed code. Operators SHALL see the code on the run, not only in logs.

#### Scenario: Denial is visible on the run
- **WHEN** admission fails with `admission.publisher`
- **THEN** the run journal contains `worker.admission_denied` with that code
