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

Adding the two columns does not change the optimistic-concurrency
contract — the same `version` column guards every write; a stale writer
still gets 409.

## MODIFIED Requirements

### Requirement: Settings shape (was: `Settings shape`)

- Before: covered `minIdle`, `maxConcurrent`, `idleTtlSeconds`,
  `approveRequired`, `knowledgeEnabled`, `verifyEnabled`, `proxyEnabled`,
  `softBudgetUsdMicros`, `hardBudgetUsdMicros`.
- After: also covers `domainType` (`Standard | Custom | Hybrid`,
  default `Standard`) and `customDomainTypesJson` (nullable `text`,
  max length 8192, default `null`).

### Requirement: REST surface (was: `REST surface`)

- Before: settings endpoints were `GET /api/v1/projects/{projectId}/settings`
  and `PUT /api/v1/projects/{projectId}/settings`.
- After: same URLs; the PUT body now also accepts `domainType` (optional;
  defaults to `Standard` when omitted) and `customDomainTypesJson`
  (optional; defaults to `null`). The GET view exposes both new fields.

## ADAPTER Notes

The resolver is a singleton — the mapping is pure (project settings in,
domain type in, profile key out) and the JSON dictionary is shared across
requests. The migration is `AddProjectDomainRouting` in the projects
schema (table `projects.project_settings`); columns `domain_type`
(`text NOT NULL DEFAULT 'Standard'`) and `custom_domain_types_json`
(`text NULL`).