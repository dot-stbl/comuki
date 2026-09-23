# Custom Domain Types — Delta

## ADDED Requirements

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

## MODIFIED Requirements

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

### Requirement: REST surface

Projects SHALL be served under `/api/v1/projects`: `POST /` (201 + view;
409 slug conflict; 400 validation), `GET /` (`?includeArchived`), `GET
/{projectId}` (404 unknown), `PATCH /{projectId}`, `DELETE /{projectId}`
(archive, 204), `GET /{projectId}/settings`, `PUT /{projectId}/settings`
(409 version conflict). The settings PUT body SHALL also accept
`domainType` (optional; defaults to `Standard` when omitted) and
`customDomainTypesJson` (optional; defaults to `null`), and the settings
GET view SHALL expose both fields. Typed exceptions become ProblemDetails
in one place; validation failures answer 400 with per-field errors.
Unknown project ids answer 404 with the projectId extension.

#### Scenario: Unknown project
- **WHEN** any project endpoint is called with an id that does not exist
- **THEN** the answer is 404 `Project not found` with the requested id
  in the extensions

#### Scenario: Settings read exposes domain-type fields
- **WHEN** a caller GETs `/api/v1/projects/{projectId}/settings` for a
  project with `domainType = Hybrid` and a non-null
  `customDomainTypesJson`
- **THEN** the response view carries both fields verbatim alongside the
  existing scale/budget/feature-flag fields

## ADAPTER Notes

The resolver is a singleton — the mapping is pure (project settings in,
domain type in, profile key out) and the JSON dictionary is shared across
requests. The migration is `AddProjectDomainRouting` in the projects
schema (table `projects.project_settings`); columns `domain_type`
(`text NOT NULL DEFAULT 'Standard'`) and `custom_domain_types_json`
(`text NULL`).