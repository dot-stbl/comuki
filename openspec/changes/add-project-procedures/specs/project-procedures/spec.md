## Purpose

Defines project procedures: layered, versioned, executable graphs that
describe how a project admits, executes, verifies, repairs, and accepts
work — compiled by a deterministic gate, pinned by runs, and proposed-to
(by the brain) but never published by anything other than an authorized
human.

## ADDED Requirements

### Requirement: Layered procedure definition

A procedure SHALL be declared in the client's git as a typed definition
whose nodes reference node kinds by catalog key and connect typed
outcome ports (see the `procedure-node-kinds` capability for the
catalog itself). A definition SHALL resolve by layering: platform
defaults, then project policy, then repository bindings that select a
procedure and pin refs, profiles, and verifier catalogs. A repository
binding SHALL NOT introduce control flow of its own; one procedure MAY
serve several repositories of one project.

#### Scenario: Repository binds to a project procedure
- **WHEN** a project connects a repository without a procedure-local definition
- **THEN** the repository executes against the project's procedure and its pinned profile catalog, and no per-repository procedure exists

#### Scenario: Unknown node kind is rejected
- **WHEN** a definition references a kind absent from the pinned kind catalog
- **THEN** the compile gate rejects it with the offending node named, and no version is published

### Requirement: Immutable published versions

Publishing a procedure SHALL produce an immutable compiled version with
a content-addressed id recording the merged definition, resolved
profiles, verifiers, and capabilities. An active run SHALL pin exactly
one compiled version. Updating a procedure SHALL NOT affect runs that
already pinned an earlier version; a retry creates a new attempt that
pins the then-current version.

#### Scenario: Update does not touch a running pin
- **WHEN** a procedure is republished while a run is executing version v4
- **THEN** the run continues on v4 to completion and its trace references v4

#### Scenario: Retry re-pins
- **WHEN** a failed task is approved for another attempt after the procedure changed
- **THEN** the new run pins the current published version and records the version change between attempts

### Requirement: Deterministic compile gate

Publication SHALL require a deterministic compile that validates
schema, acyclicity (bounded repair boundaries excluded from the static
DAG check and unrolled at runtime), capability and profile resolution
against the pinned catalogs, budget ceilings, verifier existence, and
idempotency declarations on every state-changing node. Compilation
SHALL be pure: identical inputs produce an identical compiled version.
A definition that fails any check SHALL NOT publish, and the rejection
SHALL name the failing node and rule.

#### Scenario: Cycle outside a repair boundary
- **WHEN** a draft graph contains a cycle that is not enclosed in a repair boundary
- **THEN** publication is refused and the cycle path is listed

#### Scenario: Missing verifier
- **WHEN** a verify node references a verifier absent from the pinned catalog
- **THEN** publication is refused with the missing verifier named

#### Scenario: Identical inputs, identical output
- **WHEN** the same merged definition is compiled twice
- **THEN** both compilations yield the same content-addressed version id

### Requirement: Brain proposes patches, humans publish

The brain (chat) MAY draft a `GraphPatch` against a procedure version —
add, remove, rewire, or re-parameterize nodes — as a durable proposal
with a semantic diff rendered in Studio. The brain SHALL NOT publish a
patch, SHALL NOT widen autonomy, budgets, or its own exposure, and
SHALL NOT edit an active run's graph. Publication of a patch SHALL
follow the same deterministic compile gate plus an explicit human
approval.

#### Scenario: Brain drafts a hotfix bypass
- **WHEN** an operator asks the brain to skip review for low-risk docs changes
- **THEN** the brain produces a draft GraphPatch with a rendered diff and cannot publish it; publishing requires the compile gate and a human approver

#### Scenario: Patch touches forbidden surface
- **WHEN** a drafted patch would grant the brain publish rights or raise its autonomy ceiling
- **THEN** the patch is rejected as out of scope before reaching the compile gate

### Requirement: Runtime executes the compiled plan

The runtime SHALL execute the compiled version: node readiness,
ordering, fan-out and join, and port routing are computed by the
platform, never by a model. A model participates only inside nodes
declared for it (classify, plan, review) and produces typed outputs.
No node execution SHALL be gated on a model's own decision to comply.

#### Scenario: Plan node output is validated, not trusted
- **WHEN** a plan node proposes work items that violate the procedure's profile allowlist or fan-out ceiling
- **THEN** the violating items are rejected and the outcome is recorded, and the runtime does not silently reshape the plan

### Requirement: Bounded repair boundaries

A repair boundary SHALL declare the body it re-executes, the port
outcome that re-enters it (for example verify failed), and a maximum
generation count. Exceeding the maximum, exhausting the boundary
budget, or receiving an inconclusive outcome SHALL move the owning run
to escalation for a human decision instead of looping. Generations
SHALL be numbered, reference the failing evidence, and appear in the
trace; the materialized execution graph SHALL remain acyclic.

#### Scenario: Verify fails twice then passes
- **WHEN** a verify fails, repair generation 1 re-executes the body, verify fails again, and generation 2 passes
- **THEN** the run proceeds past the boundary with both generations recorded in the trace

#### Scenario: Generations exhausted
- **WHEN** the last allowed generation still fails
- **THEN** no further generation starts, the run escalates, and a human decision offers retry, replace, waive, or fail

#### Scenario: Inconclusive does not loop
- **WHEN** a verify returns an inconclusive or infrastructure-error outcome
- **THEN** the boundary does not open a semantic repair generation; the outcome routes by its declared port

### Requirement: Human gates as decisions

A human-gate node SHALL block until a decision is recorded by
distinct authorized humans under the procedure's approval policy (zero,
one, or two, within platform floors). Gate pending state SHALL surface
in the operator attention surface with the evidence that triggered
it. A rejected or expired gate SHALL route the run by its declared
port.

#### Scenario: Two-person gate
- **WHEN** a procedure declares a two-approval gate for production effects
- **THEN** the run waits for two distinct human approvals and one person approving twice does not satisfy it

### Requirement: Planned versus observed replay

Every run SHALL keep a trace that distinguishes the compiled plan from
the observed execution, including generations, gate decisions, late or
non-authoritative results, and any dynamic work items the plan node
created. Replay SHALL present both graphs side by side and classify
divergence as within policy or outside policy according to the
procedure's declared tolerance.

#### Scenario: Plan node wrote fewer lanes than declared
- **WHEN** the plan node produced three implement lanes where the procedure example declared four
- **THEN** replay shows the divergence and classifies it within policy if it stays inside the declared fan-out range

### Requirement: Studio is a projection of the definition

The dashboard Studio SHALL render the procedure graph (React Flow
canvas with a semantic linear fallback), the validation state of the
current draft, and rendered GraphPatch diffs, reading from the
platform's compiled and draft artifacts. Studio SHALL NOT become an
authority: edits to a published definition are commits in the client's
git, and Studio's actions are limited to proposing patches, triggering
validation and dry run, and requesting publication.

#### Scenario: Operator edits a published procedure
- **WHEN** an operator wants to change a published definition from Studio
- **THEN** Studio produces a proposed GraphPatch with a diff against the pinned version and cannot apply it directly to git
