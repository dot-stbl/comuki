## Purpose

Defines reusable versioned Mission and Task work shapes independently from the procedures, profiles, and skills that execute them.

## ADDED Requirements

### Requirement: Versioned work-shape templates
Mission templates SHALL define goal/criteria fields, Task-link skeletons, completion policy, autonomy defaults, and resource requirements. Task templates SHALL define brief fields, compatible profiles, expected evidence, verifier modes, resource hints, and default relations. Templates SHALL reference pinned procedural requirements but SHALL NOT embed secret values.

#### Scenario: Instantiate Mission template
- **WHEN** a user creates a Mission from a published template version
- **THEN** the Mission records the template/version and creates a validated initial revision and Task-link skeleton

### Requirement: Template ownership and lifecycle
Templates SHALL be owned by a dedicated Templates capability and scoped to Project or curated Platform. A template follows draft, review, published, deprecated lifecycle. Editors may propose; project administrators publish Project templates; platform curation governs Platform templates.

#### Scenario: Brain drafts recurring workflow
- **WHEN** Brain identifies a repeated Project workflow
- **THEN** it may create a draft template proposal but cannot publish it without authorized human review

### Requirement: Binding and preflight
Templates SHALL declare required capabilities, profiles, models, resources, and secret metadata. Instance preflight binds available opaque references under current authorization and reports missing requirements; templates never store plaintext or assume grants.

#### Scenario: Missing secret binding
- **WHEN** a Task template requires a deployment credential not bound for the Project
- **THEN** instantiation is blocked or proposed with an actionable missing-requirement report

### Requirement: Publication verification
Publication SHALL require schema and DAG validation, capability/profile compatibility, side-effect-free dry-run, and the configured eval suite. A newly invalid dependency blocks publication.

#### Scenario: Cyclic Task skeleton
- **WHEN** a template draft contains a cycle in blocking relations
- **THEN** dry-run fails and the template cannot be published

### Requirement: Pinned instances and migrations
Existing Mission/Task instances SHALL remain pinned when a template changes. Brain MAY prepare a semantic diff and per-Task/criterion impact proposal; authorized approval creates an instance revision. Templates do not silently update active work.

#### Scenario: Published template update
- **WHEN** a newer template version changes evidence requirements
- **THEN** an active Mission remains on its pinned version until an impact proposal is approved
