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

The slug is the stable external key other modules reference. Update SHALL be partial (PATCH semantics — null fields leave stored values untouched) over name, description, profiles git URL, profiles git ref, icon, color and tags (absent tags keep the stored list; an empty array clears it); the slug SHALL NOT be editable through any endpoint.

#### Scenario: Rename without slug change

- **WHEN** a caller patches only the name
- **THEN** the name changes and every other field, including the slug, stays

#### Scenario: Patch identity only

- **WHEN** a caller patches `icon`, `color` and `tags` and nothing else
- **THEN** those fields change and the name, slug and git fields stay

### Requirement: Soft archive

Archiving SHALL be soft: the row stays for history with `archived_at` stamped; archiving twice is a no-op. Listing SHALL skip archived projects by default and include them only when `includeArchived` is requested. The archive endpoint answers 204; archived projects keep their runs and settings.

#### Scenario: Archived disappears from default list
- **WHEN** a project is archived and the list is read without the flag
- **THEN** the project is absent, and present again under `?includeArchived=true`

### Requirement: Settings shape

Per-project settings SHALL cover scale quotas (`minIdle`, `maxConcurrent`,
`idleTtlSeconds` with null meaning "engine default"), the approval gate
(`approveRequired`), the opt-in feature flags (`knowledgeEnabled`,
`verifyEnabled`, `proxyEnabled`), budget caps (`softBudgetUsdMicros`,
`hardBudgetUsdMicros` with null meaning unlimited; 1 USD = 1_000_000
micros), and the domain-type routing mode (`domainType`: `Standard |
Custom | Hybrid`, default `Standard`) with its companion
`customDomainTypesJson` (nullable `text`, max length 8192, default
`null`). One settings row per project, created with it. Soft exceedance
is advisory; hard exceedance cancels attributed runs via the costs
budget gate.

#### Scenario: Fresh project defaults
- **WHEN** a project is created
- **THEN** its settings row exists with the approval gate off, every
  feature flag off, unlimited soft/hard budgets (null), the
  engine-default idle TTL, `domainType = Standard`, and
  `customDomainTypesJson = null`

#### Scenario: Budget caps stored
- **WHEN** settings are updated with soft and hard USD micros
- **THEN** subsequent costs/budget reads observe the new caps without
  restart

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

Projects SHALL be served under `/api/v1/projects`: `POST /` (201 + view;
409 slug conflict; 400 validation), `GET /` (`?includeArchived`), `GET
/{projectId}` (404 unknown), `PATCH /{projectId}`, `DELETE /{projectId}`
(archive, 204), `GET /{projectId}/settings`, `PUT /{projectId}/settings`
(409 version conflict). The create and PATCH bodies SHALL also accept
the optional identity fields `icon`, `color` and `tags`, and every
project view SHALL expose `icon`, `color` and `tags` (icon/colour
nullable, tags a possibly-empty array) alongside the existing fields.
The settings PUT body SHALL also accept `domainType` (optional; defaults
to `Standard` when omitted) and `customDomainTypesJson` (optional;
defaults to `null`), and the settings GET view SHALL expose both fields.
Typed exceptions become ProblemDetails in one place; validation
failures answer 400 with per-field errors. Unknown project ids answer
404 with the projectId extension.

#### Scenario: Unknown project

- **WHEN** any project endpoint is called with an id that does not exist
- **THEN** the answer is 404 `Project not found` with the requested id
  in the extensions

#### Scenario: View carries identity fields

- **WHEN** a caller GETs a project created before this change
- **THEN** the view answers `icon: null`, `color: null` and `tags: []`

#### Scenario: Settings read exposes domain-type fields

- **WHEN** a caller GETs `/api/v1/projects/{projectId}/settings` for a
  project with `domainType = Hybrid` and a non-null
  `customDomainTypesJson`
- **THEN** the response view carries both fields verbatim alongside the
  existing scale/budget/feature-flag fields

### Requirement: Project visual identity

A project SHALL carry three optional identity fields, stored with the
project and served on every project view:

- `icon` — nullable opaque display string, max length 200 (a single emoji
  or an image URL; the server does not interpret which).
- `color` — nullable accent colour matching `^#[0-9A-Fa-f]{6}$`; accepted
  in either case, stored and served lower-case (`#rrggbb`).
- `tags` — list of slug-like tags, each matching
  `^[a-z0-9][a-z0-9-]{0,38}$` after trimming and lower-casing, at most 20
  distinct tags per project.

On create and update the server SHALL normalise tags (trim each entry,
lower-case invariantly, drop empty entries, remove duplicates) and colour
casing before persisting. An absent field on create means `null` icon,
`null` colour and an empty tag list.

#### Scenario: Create with identity fields

- **WHEN** a project is created with `icon = "🛰️"`,
  `color = "#3C5A86"` and `tags = ["web", "Billing"]`
- **THEN** subsequent reads return the icon verbatim, the colour as
  `#3c5a86`, and the tags as `["web", "billing"]`

#### Scenario: Duplicate and padded tags collapse

- **WHEN** a create or PATCH sends
  `tags = [" web ", "web", "", "WEB"]`
- **THEN** the stored tag list is `["web"]` — one entry, trimmed and
  lower-cased

#### Scenario: Oversized tag list refused

- **WHEN** a PATCH sends 21 distinct valid tags
- **THEN** the answer is 400 with a per-field error on `tags` and nothing
  is persisted

#### Scenario: Malformed colour refused

- **WHEN** a create sends `color = "3c5a86"` (missing `#`) or
  `"#3C5A8"` (five digits)
- **THEN** the answer is 400 with a per-field error on `color`

#### Scenario: Malformed tag refused

- **WHEN** a create sends a tag `Data_Pipeline` (underscore) or
  `-infra` (leading dash)
- **THEN** the answer is 400 with a per-field error on `tags`

#### Scenario: Oversized icon refused

- **WHEN** a create sends an `icon` longer than 200 characters
- **THEN** the answer is 400 with a per-field error on `icon`

### Requirement: Identity PATCH semantics

Update SHALL treat the three identity fields with the same PATCH
semantics as the other project fields — `null` leaves the stored value
untouched — with one wire-level distinction for the list field: an absent
(`null`) `tags` field leaves the stored tags unchanged, while an empty
array `[]` clears them. `null` icon or colour on a PATCH means
"untouched"; an explicit empty string is invalid (falsy is not null) and
answers 400 the same way it does for name.

#### Scenario: Patch colour only

- **WHEN** a caller patches only `color`
- **THEN** the colour changes and icon and tags stay as they were

#### Scenario: Empty tags list clears tags

- **WHEN** a caller patches `tags = []`
- **THEN** subsequent reads return an empty tag list

#### Scenario: Absent tags field keeps tags

- **WHEN** a caller patches `name` only, with no `tags` key in the body
- **THEN** the stored tags are unchanged

### Requirement: Identity marks in the registry

The dashboard SHALL render each project with an identity mark and its
tags: the registry list, the create/edit forms and the project detail
page all participate. The mark resolves in this order:

1. the stored `icon`, when present — an emoji renders as text, a URL
   renders as an image;
2. otherwise a mark derived client-side from the `ProfilesGitUrl` host —
   `github.com` yields the GitHub mark, a `gitlab*` host yields the
   GitLab logo, any other host a generic git glyph;
3. no `ProfilesGitUrl` — a neutral project glyph.

The stored `color`, when present, SHALL paint the small accent dot beside
the mark (registry, detail) and the tag chips' tint; it SHALL NOT paint
chrome (buttons, links, focus rings) — those stay on theme tokens. When
absent, the dot and chips fall back to the muted ink of the surrounding
data surface. The colour is decoration, never the only carrier of
meaning: slug and name always identify the row alongside it.

#### Scenario: Derived mark for a GitHub-hosted project

- **WHEN** the registry renders a project with no stored icon and
  `ProfilesGitUrl = "git@github.com:org/worker-profiles.git"`
- **THEN** the identity cell shows the GitHub mark

#### Scenario: Stored icon wins over derivation

- **WHEN** a project has `icon = "🛰️"` and a GitHub profiles URL
- **THEN** the identity cell shows the emoji, not the GitHub mark

#### Scenario: Accent dot follows the stored colour

- **WHEN** a project has `color = "#3c5a86"`
- **THEN** its registry row and detail identity block show an accent dot
  in that colour, and other rows' dots stay on the fallback ink

### Requirement: Tag filtering in the registry

The projects registry toolbar SHALL offer tag filtering over the loaded
list: a multi-select of the tags present across visible projects;
selecting one or more tags narrows the table to rows carrying **all**
selected tags (AND semantics). Filtering SHALL run client-side over the
full list the registry already loaded — the list endpoint gains no new
query parameters for tags. The tag filter composes with the existing
text filter (both must match) and the shown-count reflects the narrowed
set.

#### Scenario: Filter by one tag

- **WHEN** the operator selects the tag `billing`
- **THEN** only rows whose tag list contains `billing` remain, and the
  shown count equals that number of rows

#### Scenario: Two tags intersect

- **WHEN** the operator selects `billing` and `web` and exactly one
  project carries both
- **THEN** the table shows that one project only

#### Scenario: Clearing the tag filter

- **WHEN** the operator clears the tag selection
- **THEN** the table returns to the previous text-filter-only state

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

### Requirement: Project domain-type mode

The project settings SHALL carry a `domain_type` flag with three values:
`Standard` (default), `Custom`, `Hybrid`. `Standard` projects route every
domain to the default profile. `Custom` projects route every domain through
a per-project `custom_domain_types_json` map. `Hybrid` projects try the
default first, fall back to the JSON map.

#### Scenario: Fresh project defaults

- **WHEN** a project is created
- **THEN** its settings row has `domain_type = Standard` and
  `custom_domain_types_json IS NULL`

#### Scenario: Switching to Hybrid

- **WHEN** the operator PUTs settings with `domain_type = Hybrid` and a
  JSON map `{ "code": "implement", "data": "data-pipeline" }`
- **THEN** the next settings read returns both fields, the row's `version`
  is bumped, and the JSON is stored verbatim

### Requirement: Custom domain-type resolver

The Projects module SHALL expose an `IProjectDomainTypeResolver` that maps
a domain-type string to a profile key. The resolver SHALL throw a typed
`ProjectDomainTypeNotMappedException` when the domain type cannot be
resolved under the project's declared mode.

#### Scenario: Standard project

- **WHEN** the resolver is asked for `data` on a Standard project
- **THEN** it returns the default profile key (`implement`)

#### Scenario: Custom project with a hit

- **WHEN** the resolver is asked for `code` on a Custom project whose
  JSON maps `code → implement`
- **THEN** it returns `implement`

#### Scenario: Custom project with a miss

- **WHEN** the resolver is asked for `data` on a Custom project whose
  JSON does not have a `data` key
- **THEN** it throws `ProjectDomainTypeNotMappedException`

#### Scenario: Hybrid falls back

- **WHEN** the resolver is asked for `data` on a Hybrid project whose
  JSON has no `data` key
- **THEN** it returns the default profile key (`implement`)

#### Scenario: Hybrid has a hit

- **WHEN** the resolver is asked for `data` on a Hybrid project whose
  JSON maps `data → data-pipeline`
- **THEN** it returns `data-pipeline` (the JSON wins)

### Requirement: Settings JSON bound by length

`custom_domain_types_json` SHALL be stored as a `text` column with an
8 KiB cap (max length 8192) and SHALL only be accepted by the API when it
is either `null` or valid JSON whose top-level value is a JSON object
(string → string). The validator rejects everything else with a 400.

#### Scenario: Oversized JSON

- **WHEN** the operator PUTs settings with `custom_domain_types_json` of
  length > 8192
- **THEN** the validator answers 400 with a per-field error pointing at
  `customDomainTypesJson` and nothing is persisted

### Requirement: Settings optimistic concurrency is unchanged

Adding `domainType` and `customDomainTypesJson` SHALL NOT change the
optimistic-concurrency contract: the same `version` column guards every
write, including one that only touches the two new fields, and a stale
writer SHALL still get 409 `Settings version conflict`.

#### Scenario: Stale writer on a domain-type-only change
- **WHEN** two editors both read `version = 3` and both save with only
  `domainType` changed
- **THEN** the first save bumps `Version` to 4 and the second answers
  409 with `currentVersion = 4`, exactly as any other settings field
  conflict would

## ADAPTER Notes

Tables `projects` and `project_settings` under a module-private migrations history (`__comuki_projects`). Permission demands on these endpoints are not yet wired (a tracked TODO in the host — the identity enforcement filter is in place host-wide); current behavior is unauthenticated access until that lands.
