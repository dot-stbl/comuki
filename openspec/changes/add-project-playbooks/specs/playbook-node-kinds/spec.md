## Purpose

Defines the typed catalog of playbook node kinds: the descriptors a
playbook graph references, the execution surface each kind binds to,
the outcome ports and evidence it declares, and how the catalog is
validated, versioned, and gated by edition.

## ADDED Requirements

### Requirement: Typed kind descriptors

A node kind SHALL be declared as a typed descriptor in the
control-plane git with: a stable key; a parameter schema; typed
outcome ports (each port a named result contract, never a free-form
expression); required evidence types an instance must produce; the
owning execution surface (work item, decision, capability operation,
or brain operation); policy metadata (risk class, idempotency
requirement, approval floor); and an optional editions feature key.
The platform baseline catalog SHALL ship a fixed v1 set: intake,
classify, plan, agent, fan-out, join, verify, review, human-gate,
repair-boundary, capability, complete, escalate.

#### Scenario: Baseline catalog resolves
- **WHEN** a playbook references `verify` with its standard parameter schema
- **THEN** the descriptor resolves from the pinned catalog with its declared outcome ports (`passed`, `failed`, `inconclusive`, `infrastructure-error`)

#### Scenario: Descriptor missing an owner
- **WHEN** a catalog draft declares a kind without an owning execution surface
- **THEN** catalog validation refuses the draft and names the kind

### Requirement: The graph engine dispatches, kinds execute

The playbook engine SHALL interpret wiring (ports, ordering,
generations) and dispatch node instances to the kind's owning surface;
it SHALL NOT interpret kind internals. A kind's execution semantics
live with its owner: an `agent` node instance is a work item under
lease, a `human-gate` instance is a decision, a `capability` instance
is a broker operation. Instance state SHALL be read from the owner
through a typed projection; the coordinator tracks
position-in-procedure and owner references only.

#### Scenario: Capability node routes through the broker
- **WHEN** a `capability` node instance becomes ready
- **THEN** the coordinator starts a broker operation for it and never calls the target capability directly

### Requirement: Catalog validation and versioning

Catalog changes SHALL validate descriptor schemas and owner
resolvability before entering a catalog version. A compiled playbook
version SHALL pin the catalog version it resolved against; later
catalog changes SHALL NOT affect active pins. Adding a kind or
altering ports, parameters, or evidence requirements SHALL produce a
new catalog version; retracting a kind referenced by any published
playbook SHALL be refused.

#### Scenario: Republished playbook keeps its catalog pin
- **WHEN** a catalog gains a new kind while a run executes a playbook pinned to catalog v1
- **THEN** the run's node dispatch still resolves against v1

#### Scenario: Retraction blocked by reference
- **WHEN** an owner tries to retract `repair-boundary` while any published playbook references it
- **THEN** the retraction is refused with the referencing playbooks named

### Requirement: Editions enforcement at kind granularity

A kind descriptor MAY carry an editions feature key. The compile gate
SHALL refuse a graph using a kind whose feature key the effective
edition does not grant, naming the offending node and the missing
key. Refusal happens at compile time — never mid-run — so a pinned
version always remains executable under the edition that compiled it.

#### Scenario: Two-approval gate on community
- **WHEN** a draft graph uses a gate kind carrying `playbooks.two-approval-gates` under a community edition
- **THEN** the compile gate refuses publication and names the node and the feature key

#### Scenario: Paid kind inside an active pin after downgrade
- **WHEN** an edition lapses to community while runs pinned under the enterprise edition are still executing
- **THEN** those runs continue to completion; only new compilations are subject to the refusal
