# Scope Layers Specification

## Purpose

Defines the four-level precedence resolver for project settings: orchestra → section → worker → card. Defines the `Section` aggregate (a new sibling of `Project`), the first *write* path to global (`PATCH /api/v1/settings`), the precedence adapter that reads through the existing `IProjectSettingsSnapshotCache` pattern, and the wiring of the four `ProjectSettings` bool flags with zero runtime consumers onto their real consumer paths.

Today `ProjectSettings` is the only settings surface. The four bool flags (`ApproveRequired` / `KnowledgeEnabled` / `VerifyEnabled` / `ProxyEnabled`) have zero runtime consumers; the two budget-side fields (`softBudgetUsdMicros` / `hardBudgetUsdMicros`) are already consumed by `Comuki.Host/Costs/ProjectBudgetSettingsAdapter.cs`. This capability makes the four bool flags real and makes global settings mutable for the first time.

## ADDED Requirements

### Requirement: Four layers in fixed precedence

The precedence chain SHALL be `orchestra → section → worker → card`, evaluated top-to-bottom with the first non-`null` value winning. `null` at any layer means "use the layer below"; a `null` at every layer means "use the platform default". The chain is implemented as `ProjectSettingsResolver` — a single `IProjectSettingsSnapshotCache`-style seam (the documented template from `ProjectScaleSettingsAdapter`).

#### Scenario: Orchestra overrides everything

- **WHEN** an orchestra value is set and section / worker / card values exist
- **THEN** the precedence resolver returns the orchestra value

#### Scenario: Section override with no orchestra

- **WHEN** an orchestra value is `null` and a section value is set
- **THEN** the precedence resolver returns the section value

#### Scenario: Worker overrides section with no orchestra

- **WHEN** orchestra and section are `null` and a worker value is set
- **THEN** the precedence resolver returns the worker value

#### Scenario: Card override is deferred

- **WHEN** all three populated layers are `null` and the card layer is `null`
- **THEN** the precedence resolver returns the platform default for the requested field
- **AND** the card layer is not yet populated; its implementation rides the mission-cowork card entity landing and is filed as a follow-up in `design.md` §1

### Requirement: Section is a new sibling aggregate

`Section` is a new aggregate under `Comuki.Modules.Projects.Sections` (Domain / Application / Infrastructure). The aggregate carries `Id`, `Slug`, `ProjectId`, `Name`, `ArchivedAt`. Workers carry a `SectionId` column on the `Claim` row (`worker.section_id`, nullable). A worker assigned to a section has precedence over the project-wide layer; a worker without a section assignment reads the project-wide layer only.

#### Scenario: Section creates a group

- **WHEN** an operator with `project:write` creates a Section named `build-farm` for project `acme`
- **THEN** the Section row lands in `sections` and the section is visible under the project's settings

#### Scenario: Worker reads section overrides

- **WHEN** a worker is assigned to a Section with `ApproveRequired = true`
- **THEN** the precedence resolver returns `ApproveRequired = true` for that worker's claim and the worker's approval gate is enforced

### Requirement: Orchestra is the first write path to global

`GET /api/v1/settings` SHALL be promoted to `PATCH /api/v1/settings` with optimistic concurrency on a single `OrchestraSettings` row (one per platform). The body shape is the same `ProjectSettings`-shaped field set; a `null` value clears the orchestra override and falls through to the section layer. The endpoint requires `platform:write` (a new permission; granted only to `PlatformAdmin` / `Operator`; see the `identity` capability for the canonical access model — `platform:write` is the canonical key, not duplicated under `projects:write` or any other existing permission). The existing `Settings version conflict` 409 contract (`projects/spec.md`) extends to orchestra.

#### Scenario: Orchestra settings patch succeeds

- **WHEN** an authorised caller PATCHes `OrchestraSettings` with `{ "approveRequired": true }`
- **THEN** the response is `200` with the updated view and the precedence resolver reads the new value

#### Scenario: Stale orchestra write returns 409

- **WHEN** two writers both read version 1 of `OrchestraSettings` and both PATCH
- **THEN** the first wins and the second answers `409` with `currentVersion = 2`

#### Scenario: Null orchestra value falls through

- **WHEN** an authorised caller PATCHes `OrchestraSettings` with `{ "approveRequired": null }`
- **THEN** the orchestra layer's `ApproveRequired` is cleared and the precedence resolver reads the section value (or the worker value, or the platform default)

### Requirement: Each ProjectSettings flag has one consumer

Each `ProjectSettings` field SHALL be read by exactly one consumer; this capability records the contract. The four unused bool flags gain their first consumer path in this phase; `VerifyEnabled` was wired in the `verification` capability phase and is confirmed here with an integration test that reads the precedence and observes the consumer reading the right value. The two budget-side fields are already consumed by `ProjectBudgetSettingsAdapter`; they are not part of this capability.

| Flag | Consumer | Behaviour |
|------|----------|-----------|
| `ApproveRequired` | run-approval gate | when `true`, terminal `Escalated` runs require explicit human approval |
| `KnowledgeEnabled` | knowledge surface | when `true`, the project is part of the cross-project memory pool |
| `VerifyEnabled` | verification path | when `true`, gates are required (wired in Phase 3, confirmed here) |
| `ProxyEnabled` | proxy resolution | when `true`, the project may use mutable proxy keys (Tuner) |
| `softBudgetUsdMicros` | costs budget gate | advisory; surfaces in dashboards |
| `hardBudgetUsdMicros` | costs budget gate | cancels attributed runs when exceeded |

#### Scenario: ApproveRequired honoured at the gate

- **WHEN** a Run reaches `Escalated` for a project with `ApproveRequired = true`
- **THEN** the run stays `Escalated` until an authorised caller hits `POST /api/v1/runs/{runId}/approve`; auto-archive ratchets (per `autonomy-escalation-timeout`) do not advance without approval

#### Scenario: KnowledgeEnabled flags the project in the cross-project pool

- **WHEN** a project has `KnowledgeEnabled = true` and a knowledge-base assertion lands in the project
- **THEN** the assertion participates in the cross-project memory pool; when `false`, it stays project-private

### Requirement: Editions limits and scope layers are independent axes

Editions limits (`Limits.Projects = 1` in Community) and the `scope-layers` feature key SHALL NOT cross-reference. A Community deployment with `scope-layers` enabled SHALL resolve the four-layer chain exactly as a paid deployment does; only the *limit* on project creation differs. The architecture test `EditionsRegistryContainsEveryGateKeyShould` does not need to cross-reference either axis; the docs record the independence.

#### Scenario: Community can use scope layers

- **WHEN** a Community deployment reads the orchestra/section/worker chain
- **THEN** the precedence resolver returns the same result as a paid deployment for the same configuration

#### Scenario: Limit enforcement is independent

- **WHEN** a Community deployment with `scope-layers` enabled tries to create a second project
- **THEN** the response is `403 edition.limit_exceeded` with `limitKey = projects` regardless of the orchestra/section/worker values

## ADAPTER Notes

`IProjectSettingsSnapshotCache` is the existing pattern this capability clones. `ProjectScaleSettingsAdapter` is the documented template. The four unused bool flags live on the existing `ProjectSettings` table; the field names, validation, and `null`-means-default semantics are unchanged. Editions limits and scope layers are *independent axes* by separate changes.
