## MODIFIED Requirements

### Requirement: Project creation with unique slug

Creating a project SHALL normalize the slug (trim, lower-case) and refuse duplicates loudly (409 conflict); the unique index backs the check, so a concurrent create loses with a database error instead of a duplicate. A new project SHALL be created together with its default settings row in one unit of work. Defaults: `MinIdle` 0, `MaxConcurrent` 4 (mirroring the supervisor options default), `IdleTtlSeconds` null (engine default), all feature flags and the approval gate off, `Version` 1.

> **Coordination note (2026-10-04).** This change's **Section** phase adds the `Section` aggregate as a sibling of `Project`. The slug is still unique *per project*; Sections add a `(projectId, slug)` uniqueness on top — sections are scoped to their parent project. The phase does not fork the slug contract; it adds the section scope.

#### Scenario: Duplicate slug refused

- **WHEN** a project is created with an already-taken slug
- **THEN** the API answers 409 and no row is written

### Requirement: Slug is immutable

The slug is the stable external key other modules reference. Update SHALL be partial (PATCH semantics — null fields leave stored values untouched) over name, description, profiles git URL, profiles git ref, icon, color and tags (absent tags keep the stored list; an empty array clears it); the slug SHALL NOT be editable through any endpoint.

#### Scenario: Rename without slug change

- **WHEN** a caller patches only the name
- **THEN** the name changes and every other field, including the slug, stays

#### Scenario: Patch identity only

- **WHEN** a caller patches `icon`, `color` and `tags` and nothing else
- **THEN** those fields change and the name, slug and git fields stay

### Requirement: Soft archive

Archiving SHALL be soft: the row stays for history with `archived_at` stamped; archiving twice is a no-op. Listing SHALL skip archived projects by default and include them only when `includeArchived` is requested. The archive endpoint answers 204; archived projects keep their runs and settings.

> **Coordination note (2026-10-04).** The **Section** phase introduces `SectionArchiveAt`. Sections archive alongside their parent project; a section's `archived_at` is independent of the project's archive timestamp.

#### Scenario: Archived disappears from default list

- **WHEN** a project is archived and the list is read without the flag
- **THEN** the project is absent, and present again under `?includeArchived=true`

### Requirement: Settings shape

Per-project settings SHALL cover scale quotas (`minIdle`, `maxConcurrent`, `idleTtlSeconds` with null meaning "engine default"), the approval gate (`approveRequired`), the opt-in feature flags (`knowledgeEnabled`, `verifyEnabled`, `proxyEnabled`), the optional harness id (`harnessId`, defaulting to `"pi"`), and budget caps (`softBudgetUsdMicros`, `hardBudgetUsdMicros` with null meaning unlimited; 1 USD = 1_000_000 micros). One settings row per project, created with it. Soft exceedance is advisory; hard exceedance cancels attributed runs via the costs budget gate.

> **Coordination note (2026-10-04).** This change's phases wire the previously-unused flags to their first consumer paths: `VerifyEnabled` (verification phase), `ProxyEnabled` (model-control phase), `ApproveRequired` (scope-layers phase). `HarnessId` is new and rides the **harness-spi** phase. The settings table above is unchanged in shape; this modification documents the wiring. The two budget-side fields (`softBudgetUsdMicros` / `hardBudgetUsdMicros`) are already consumed by `Comuki.Host/Costs/ProjectBudgetSettingsAdapter.cs` and are not touched by this change.

#### Scenario: Fresh project defaults

- **WHEN** a project is created
- **THEN** its settings row exists with the approval gate off, every feature flag off, `harnessId = "pi"`, unlimited soft/hard budgets (null), and the engine-default idle TTL

#### Scenario: Budget caps stored

- **WHEN** settings are updated with soft and hard USD micros
- **THEN** subsequent costs/budget reads observe the new caps without restart

### Requirement: Settings optimistic concurrency → 409

Every settings mutation SHALL bump `Version` (starts at 1). A writer SHALL present the version it read; a mismatch (stale writer) SHALL answer 409 `Settings version conflict` carrying the current version and a re-read-and-retry hint. The store re-checks expected-version (mutated entity must be exactly current+1) and the database concurrency token catches remaining races — a lost writer race surfaces as the same typed 409, never a silent lost update. A save for a missing row with version above 1 SHALL also 409 (refuse to resurrect deleted rows).

> **Coordination note (2026-10-04).** The same optimistic-concurrency contract extends to the `OrchestraSettings` row the **scope-layers** phase introduces. Two writers racing the orchestra row surface the same 409.

#### Scenario: Concurrent editors

- **WHEN** two editors both read version 3 and both save
- **THEN** the first wins and the second answers 409 with `currentVersion = 4`

#### Scenario: Deleted-row resurrection refused

- **WHEN** a save targets a project whose row disappeared after the read
- **THEN** the store answers 409 rather than re-inserting at a stale version

### Requirement: Settings live reload

Settings changes SHALL apply without a restart through a shared snapshot cache: every write replaces the cached entry AND fires the project's change token; a background refresher re-reads all rows at startup and then every 15 seconds. Cache entries carry a 30-second absolute TTL so a dead refresher degrades to "not cached" instead of serving stale values forever. Read-path fills (warm) SHALL NOT announce changes; only writes (refresh) fire the token.

#### Scenario: PUT visible on the next supervisor pass

- **WHEN** settings are updated via the API
- **THEN** the next scale supervisor pass reads the new values from the refreshed cache

#### Scenario: Restart keeps cache warm

- **WHEN** the host restarts
- **THEN** the refresher's first pass warms every settings row before the supervisor's first poll needs it

### Requirement: Sync snapshot read for compute

The settings store SHALL expose a synchronous `GetCached` snapshot read (memory-only, never touches the database; null when not yet cached) for consumers that cannot await — the compute scale adapter. Callers fall back to their own defaults on a miss.

> **Coordination note (2026-10-04).** The **scope-layers** phase introduces `ProjectSettingsResolver`, a single `IProjectSettingsSnapshotCache`-style seam that reads through the four-layer precedence chain (orchestra → section → worker → card). The resolver delegates to the existing `GetCached` for the worker and section layers and to a single-row read for the orchestra layer.

#### Scenario: Uncached project uses defaults

- **WHEN** the supervisor asks for a project with no cached row
- **THEN** the adapter answers the supervisor option defaults

### Requirement: Scale adapter bridges modules to the engine

The host SHALL bridge the Projects settings store onto the engine's per-project scale settings port (modules must not reference the engine). `Get` SHALL map the cached row (null idle TTL → engine default) or the option defaults when uncached. `Override` SHALL throw `NotSupportedException` — in-process overrides no longer exist; writes go exclusively through the settings API, which refreshes the cache the adapter reads.

#### Scenario: Override is refused

- **WHEN** anything calls the adapter's override
- **THEN** it throws with a pointer to the settings API

### Requirement: REST surface

Projects SHALL be served under `/api/v1/projects`: `POST /` (201 + view; 409 slug conflict; 400 validation), `GET /` (`?includeArchived`), `GET /{projectId}` (404 unknown), `PATCH /{projectId}`, `DELETE /{projectId}` (archive, 204), `GET /{projectId}/settings`, `PUT /{projectId}/settings` (409 version conflict). The create and PATCH bodies SHALL also accept the optional identity fields `icon`, `color` and `tags`, and every project view SHALL expose `icon`, `color` and `tags` (icon/colour nullable, tags a possibly-empty array) alongside the existing fields. Typed exceptions become ProblemDetails in one place; validation failures answer 400 with per-field errors. Unknown project ids answer 404 with the projectId extension.

> **Coordination note (2026-10-04).** The **scope-layers** phase adds `/api/v1/settings` (orchestra write path) and `/api/v1/projects/{projectId}/sections` (CRUD for sections); neither path replaces the existing `/api/v1/projects` surface.

#### Scenario: Unknown project

- **WHEN** any project endpoint is called with an id that does not exist
- **THEN** the answer is 404 `Project not found` with the requested id in the extensions

#### Scenario: View carries identity fields

- **WHEN** a caller GETs a project created before this change
- **THEN** the view answers `icon: null`, `color: null` and `tags: []`

### Requirement: Scalar EnvClass until Repository lands

A Project SHALL carry `EnvClass` (catalog id or empty) as the stand-in binding for its scalar source repository. Create and PATCH SHALL accept it with null-means-untouched semantics. Empty `EnvClass` SHALL make implement work items for that source unclaimable. When `add-multi-repo-projects` migrates `SourceGitUrl` to a primary `ProjectRepositoryAttachment`, `EnvClass` SHALL move onto that Repository row and the Project field SHALL cease to be source of truth. `ProfilesGitUrl` remains unrelated.

#### Scenario: Create with class

- **WHEN** a project is created with `envClass = "net10-sdk-bun"`
- **THEN** subsequent reads return that class and implement items copy it at enqueue

#### Scenario: Empty class blocks implement

- **WHEN** a project has `SourceGitUrl` set and `EnvClass` empty
- **THEN** implement work items for that source are not claimable

#### Scenario: Migration moves the field

- **WHEN** the primary-attachment migration runs for a project with `EnvClass = "net10-sdk"`
- **THEN** the registered Repository carries `EnvClass = "net10-sdk"` and enqueue reads the Repository, not the Project scalar

## ADDED Requirements

### Requirement: Sections are a per-project sibling aggregate

`Section` is a new sibling aggregate under `Comuki.Modules.Projects.Sections` (Domain / Application / Infrastructure). The aggregate carries `Id`, `Slug`, `ProjectId`, `Name`, `ArchivedAt`. Workers carry a nullable `SectionId` column on the `Claim` row (`worker.section_id`). The REST surface is `/api/v1/projects/{projectId}/sections` (list / create / patch / archive), requiring `project:write` for writes and `project:read` for reads. The slug is unique per project `(project_id, slug)`; sections are scoped to their parent project.

#### Scenario: Section creates a group

- **WHEN** an authorised caller POSTs a section named `build-farm` for project `acme` with `slug: "build-farm"`
- **THEN** the response is `201` with the persisted view and the row is unique on `(acme, build-farm)`

#### Scenario: Duplicate section slug within a project refused

- **WHEN** a caller POSTs a second section with `slug: "build-farm"` for the same project
- **THEN** the response is `409` and no row is written

#### Scenario: Section slug may collide across projects

- **WHEN** two projects both have a section named `build-farm`
- **THEN** both rows exist independently; uniqueness is `(project_id, slug)`, not global

### Requirement: Worker section assignment flows through the claim

A worker assigned to a Section has precedence over the project-wide layer; a worker without a Section assignment reads the project-wide layer only. The claim engine reads the worker's `SectionId` and threads it through the four-layer precedence resolver (`scope-layers` capability).

#### Scenario: Worker reads section overrides

- **WHEN** a worker is assigned to a Section with `ApproveRequired = true` and the project-wide layer is `ApproveRequired = false`
- **THEN** the precedence resolver returns `ApproveRequired = true` for that worker's claim and the approval gate is enforced

#### Scenario: Worker without section reads project-wide

- **WHEN** a worker has `SectionId = null` and the project-wide layer is `ApproveRequired = true`
- **THEN** the precedence resolver returns `ApproveRequired = true`

### Requirement: Orchestra is the first write path to global

> **Coordination note (2026-10-04).** The orchestra write path (`PATCH /api/v1/settings`) and the four-layer precedence resolver are owned by the **`scope-layers` capability** — the canonical home for these surfaces. The `projects` capability references scope-layers here rather than duplicating the requirement; the section aggregate, the `project:write` permission, and the existing `Projects` REST surface stay owned by the `projects` capability.

The scope-layers capability introduces the orchestra write path: `GET /api/v1/settings` SHALL be promoted to `PATCH /api/v1/settings` with optimistic concurrency on a single `OrchestraSettings` row (one per platform). The body shape is the same `ProjectSettings`-shaped field set; a `null` value clears the orchestra override and falls through to the section layer. The endpoint requires `platform:write` (a new permission; granted only to `PlatformAdmin` / `Operator`). See `scope-layers/spec.md` §"Requirement: Orchestra is the first write path to global" for the canonical scenarios (Orchestra settings patch succeeds, Stale orchestra write returns 409, Null orchestra value falls through).

#### Scenario: Orchestra settings patch succeeds (cross-reference)

- **WHEN** an authorised caller PATCHes `OrchestraSettings` with `{ "approveRequired": true }`
- **THEN** the response is `200` with the updated view and the precedence resolver reads the new value
- (The `scope-layers` capability owns the full scenario set; this cross-reference scenario is required for the OpenSpec "every ADDED Requirement needs at least one scenario" rule — the canonical scenarios are in `scope-layers/spec.md`.)

### Requirement: Harness id is a new project setting

`ProjectSettings.HarnessId` (a new optional field, default `"pi"`) is the project-level harness selection. The harness abstraction (`harness-spi` capability) reads the value through the same precedence resolver. A profile's `harness:` frontmatter overrides the project setting; the claim engine matches on the harness label the same way it matches on `envClass`.

#### Scenario: Default harness is pi

- **WHEN** a project has no explicit `HarnessId`
- **THEN** claims default to `pi` and the existing pi spawning path runs

#### Scenario: Project setting selects the test fake

- **WHEN** an operator sets `ProjectSettings.HarnessId = "test-fake-pi"`
- **THEN** claims with that project match against `test-fake-pi` workers and not against `pi` workers

### Requirement: Each ProjectSettings flag has one consumer

The four unused `ProjectSettings` bool flags (`ApproveRequired` / `KnowledgeEnabled` / `VerifyEnabled` / `ProxyEnabled`) gain their first consumer paths across the orchestra phases. The two budget-side fields (`softBudgetUsdMicros` / `hardBudgetUsdMicros`) are already consumed by `Comuki.Host/Costs/ProjectBudgetSettingsAdapter.cs`; they are not part of this change's wiring:

| Flag | Consumer | Behaviour |
|------|----------|-----------|
| `ApproveRequired` | run-approval gate | when `true`, terminal `Escalated` runs require explicit human approval |
| `KnowledgeEnabled` | knowledge surface | when `true`, the project is part of the cross-project memory pool |
| `VerifyEnabled` | verification path | when `true`, gates are required (wired in the verification phase) |
| `ProxyEnabled` | proxy resolution | when `true`, the project may use mutable proxy keys (model-control phase) |
| `softBudgetUsdMicros` | costs budget gate | advisory; surfaces in dashboards |
| `hardBudgetUsdMicros` | costs budget gate | cancels attributed runs when exceeded |

The `null`-means-default semantics is unchanged; the wiring is a read-side change.

#### Scenario: ApproveRequired honoured at the gate

- **WHEN** a Run reaches `Escalated` for a project with `ApproveRequired = true`
- **THEN** the run stays `Escalated` until an authorised caller hits `POST /api/v1/runs/{runId}/approve`; auto-archive ratchets (per `autonomy-escalation-timeout`) do not advance without approval

## ADAPTER Notes

The four unused `ProjectSettings` bool flags exist on the `ProjectSettings` table; this change wires them to their first consumer paths without renaming, retyping, or revalidating. The two budget-side fields are already consumed by `ProjectBudgetSettingsAdapter`; they are not part of this change. The optimistic-concurrency contract on `ProjectSettings` extends to `OrchestraSettings`. The `Section` aggregate is a sibling of `Project`, not a fork; the migration lives under `__comuki_projects` per the existing pattern.