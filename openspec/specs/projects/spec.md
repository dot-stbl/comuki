# Projects Specification

## Purpose

Defines the project aggregate (the scope unit for runs, work items, settings and role assignments): CRUD with immutable slugs, soft archive, per-project settings with optimistic concurrency and live reload, and the adapter that feeds project settings into the compute scale port.

## Requirements

### Requirement: Project creation with unique slug

Creating a project SHALL normalize the slug (trim, lower-case) and refuse duplicates loudly (409 conflict); the unique index backs the check, so a concurrent create loses with a database error instead of a duplicate. A new project SHALL be created together with its default settings row in one unit of work. Defaults: `MinIdle` 0, `MaxConcurrent` 4 (mirroring the supervisor options default), `IdleTtlSeconds` null (engine default), all feature flags and the approval gate off, `Version` 1.

#### Scenario: Duplicate slug refused
- **WHEN** a project is created with an already-taken slug
- **THEN** the API answers 409 and no row is written

### Requirement: Slug is immutable

The slug is the stable external key other modules reference. Update SHALL be partial (PATCH semantics — null fields leave stored values untouched) over name, description, profiles git URL and profiles git ref; the slug SHALL NOT be editable through any endpoint.

#### Scenario: Rename without slug change
- **WHEN** a caller patches only the name
- **THEN** the name changes and every other field, including the slug, stays

### Requirement: Soft archive

Archiving SHALL be soft: the row stays for history with `archived_at` stamped; archiving twice is a no-op. Listing SHALL skip archived projects by default and include them only when `includeArchived` is requested. The archive endpoint answers 204; archived projects keep their runs and settings.

#### Scenario: Archived disappears from default list
- **WHEN** a project is archived and the list is read without the flag
- **THEN** the project is absent, and present again under `?includeArchived=true`

### Requirement: Settings shape

Per-project settings SHALL cover scale quotas (`minIdle`, `maxConcurrent`, `idleTtlSeconds` with null meaning "engine default"), the approval gate (`approveRequired`), and the opt-in feature flags (`knowledgeEnabled`, `verifyEnabled`, `proxyEnabled`). One settings row per project, created with it.

#### Scenario: Fresh project defaults
- **WHEN** a project is created
- **THEN** its settings row exists with the approval gate off, every feature flag off, and the engine-default idle TTL

### Requirement: Settings optimistic concurrency → 409

Every settings mutation SHALL bump `Version` (starts at 1). A writer SHALL present the version it read; a mismatch (stale writer) SHALL answer 409 `Settings version conflict` carrying the current version and a re-read-and-retry hint. The store re-checks expected-version (mutated entity must be exactly current+1) and the database concurrency token catches remaining races — a lost writer race surfaces as the same typed 409, never a silent lost update. A save for a missing row with version above 1 SHALL also 409 (refuse to resurrect deleted rows).

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

#### Scenario: Uncached project uses defaults
- **WHEN** the supervisor asks for a project with no cached row
- **THEN** the adapter answers the supervisor option defaults

### Requirement: Scale adapter bridges modules to the engine

The host SHALL bridge the Projects settings store onto the engine's per-project scale settings port (modules must not reference the engine). `Get` SHALL map the cached row (null idle TTL → engine default) or the option defaults when uncached. `Override` SHALL throw `NotSupportedException` — in-process overrides no longer exist; writes go exclusively through the settings API, which refreshes the cache the adapter reads.

#### Scenario: Override is refused
- **WHEN** anything calls the adapter's override
- **THEN** it throws with a pointer to the settings API

### Requirement: REST surface

Projects SHALL be served under `/api/v1/projects`: `POST /` (201 + view; 409 slug conflict; 400 validation), `GET /` (`?includeArchived`), `GET /{projectId}` (404 unknown), `PATCH /{projectId}`, `DELETE /{projectId}` (archive, 204), `GET /{projectId}/settings`, `PUT /{projectId}/settings` (409 version conflict). Typed exceptions become ProblemDetails in one place; validation failures answer 400 with per-field errors. Unknown project ids answer 404 with the projectId extension.

#### Scenario: Unknown project
- **WHEN** any project endpoint is called with an id that does not exist
- **THEN** the answer is 404 `Project not found` with the requested id in the extensions

## ADAPTER Notes

Tables `projects` and `project_settings` under a module-private migrations history (`__comuki_projects`). Permission demands on these endpoints are not yet wired (a tracked TODO in the host — the identity enforcement filter is in place host-wide); current behavior is unauthenticated access until that lands.
